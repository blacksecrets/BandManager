// Take-a-photo-of-a-receipt flow for the Expenses page: live camera
// capture, then automatic document-border detection on the still frame
// (classical CV - grayscale, Otsu threshold, largest-connected-component,
// extremal-point corner finding - not a trained model, so it depends on
// the receipt having reasonable contrast against its background), with
// draggable corner handles to fix a bad guess before a perspective warp
// crops out everything outside the document. Self-contained, promise-
// based like catalog.js's openCatalogPicker - window.ReceiptCamera.open()
// resolves to a File (a cropped JPEG) or null if cancelled.
//
// Detection runs once, on the captured still - not live per-frame on the
// video stream, which would cost far more CPU for a guess the user is
// about to confirm/adjust anyway.
window.ReceiptCamera = (function () {
    // --- Image-processing internals ---

    function toGrayscaleArray(imageData) {
        const { data, width, height } = imageData;
        const gray = new Uint8ClampedArray(width * height);
        for (let i = 0, p = 0; i < data.length; i += 4, p++) {
            gray[p] = (data[i] * 0.299 + data[i + 1] * 0.587 + data[i + 2] * 0.114) | 0;
        }
        return gray;
    }

    // Standard Otsu's method - the threshold that maximizes between-class
    // variance of a bimodal grayscale histogram, i.e. the best automatic
    // split between "document" and "background" brightness.
    function otsuThreshold(gray) {
        const hist = new Array(256).fill(0);
        for (let i = 0; i < gray.length; i++) hist[gray[i]]++;
        const total = gray.length;
        let sum = 0;
        for (let t = 0; t < 256; t++) sum += t * hist[t];

        let sumB = 0, wB = 0, maxVar = 0, threshold = 127;
        for (let t = 0; t < 256; t++) {
            wB += hist[t];
            if (wB === 0) continue;
            const wF = total - wB;
            if (wF === 0) break;
            sumB += t * hist[t];
            const mB = sumB / wB;
            const mF = (sum - sumB) / wF;
            const varBetween = wB * wF * (mB - mF) * (mB - mF);
            if (varBetween > maxVar) { maxVar = varBetween; threshold = t; }
        }
        return threshold;
    }

    // Which side of the threshold is "the document" isn't fixed (a white
    // receipt on a dark table vs. a dark receipt on a light desk both
    // happen) - compare the outer border strip's average brightness
    // against the center's; whichever is darker is assumed to be the
    // background, so the document is the other class.
    function foregroundIsLightClass(gray, w, h) {
        const marginX = Math.max(1, Math.round(w * 0.06));
        const marginY = Math.max(1, Math.round(h * 0.06));
        let borderSum = 0, borderCount = 0, centerSum = 0, centerCount = 0;
        for (let y = 0; y < h; y++) {
            const inBorderRow = y < marginY || y >= h - marginY;
            for (let x = 0; x < w; x++) {
                const isBorder = inBorderRow || x < marginX || x >= w - marginX;
                const v = gray[y * w + x];
                if (isBorder) { borderSum += v; borderCount++; } else { centerSum += v; centerCount++; }
            }
        }
        if (centerCount === 0 || borderCount === 0) return true;
        return (borderSum / borderCount) < (centerSum / centerCount);
    }

    function buildMask(gray, threshold, foregroundIsLight) {
        const mask = new Uint8Array(gray.length);
        for (let i = 0; i < gray.length; i++) {
            const isLight = gray[i] >= threshold;
            mask[i] = isLight === foregroundIsLight ? 1 : 0;
        }
        return mask;
    }

    // Largest 4-connected component in the mask, plus its 4 extremal
    // points (min/max of x+y and x-y). For a convex, roughly-rectangular
    // blob those 4 extrema ARE its corners - this sidesteps needing a
    // separate contour-tracing or convex-hull pass entirely.
    function largestComponentExtrema(mask, w, h) {
        const visited = new Uint8Array(mask.length);
        const qx = new Int32Array(mask.length);
        const qy = new Int32Array(mask.length);
        let best = null;

        for (let sy = 0; sy < h; sy++) {
            for (let sx = 0; sx < w; sx++) {
                const startIdx = sy * w + sx;
                if (!mask[startIdx] || visited[startIdx]) continue;

                let head = 0, tail = 0;
                qx[tail] = sx; qy[tail] = sy; tail++;
                visited[startIdx] = 1;
                let size = 0;
                let minSum = Infinity, maxSum = -Infinity, minDiff = Infinity, maxDiff = -Infinity;
                let pMinSum, pMaxSum, pMinDiff, pMaxDiff;

                while (head < tail) {
                    const x = qx[head], y = qy[head]; head++;
                    size++;
                    const s = x + y, d = x - y;
                    if (s < minSum) { minSum = s; pMinSum = { x, y }; }
                    if (s > maxSum) { maxSum = s; pMaxSum = { x, y }; }
                    if (d < minDiff) { minDiff = d; pMinDiff = { x, y }; }
                    if (d > maxDiff) { maxDiff = d; pMaxDiff = { x, y }; }

                    const nIdxUp = (y - 1) * w + x, nIdxDown = (y + 1) * w + x;
                    const nIdxLeft = y * w + (x - 1), nIdxRight = y * w + (x + 1);
                    if (y > 0 && mask[nIdxUp] && !visited[nIdxUp]) { visited[nIdxUp] = 1; qx[tail] = x; qy[tail] = y - 1; tail++; }
                    if (y < h - 1 && mask[nIdxDown] && !visited[nIdxDown]) { visited[nIdxDown] = 1; qx[tail] = x; qy[tail] = y + 1; tail++; }
                    if (x > 0 && mask[nIdxLeft] && !visited[nIdxLeft]) { visited[nIdxLeft] = 1; qx[tail] = x - 1; qy[tail] = y; tail++; }
                    if (x < w - 1 && mask[nIdxRight] && !visited[nIdxRight]) { visited[nIdxRight] = 1; qx[tail] = x + 1; qy[tail] = y; tail++; }
                }

                if (!best || size > best.size) best = { size, pMinSum, pMaxSum, pMinDiff, pMaxDiff };
            }
        }
        return best;
    }

    // Runs the pipeline on a downscaled copy of sourceCanvas (detection
    // doesn't need full resolution) and scales the resulting corners back
    // up to sourceCanvas's real pixel space. Returns null (caller falls
    // back to a default inset rectangle) if nothing large enough was found
    // - low contrast, cluttered background, etc.
    function detectDocumentCorners(sourceCanvas) {
        const maxDim = 360;
        const scale = Math.min(1, maxDim / Math.max(sourceCanvas.width, sourceCanvas.height));
        const w = Math.max(1, Math.round(sourceCanvas.width * scale));
        const h = Math.max(1, Math.round(sourceCanvas.height * scale));

        const work = document.createElement('canvas');
        work.width = w; work.height = h;
        const wctx = work.getContext('2d');
        wctx.drawImage(sourceCanvas, 0, 0, w, h);
        const imageData = wctx.getImageData(0, 0, w, h);

        const gray = toGrayscaleArray(imageData);
        const threshold = otsuThreshold(gray);
        const mask = buildMask(gray, threshold, foregroundIsLightClass(gray, w, h));
        const extrema = largestComponentExtrema(mask, w, h);

        if (!extrema || extrema.size < w * h * 0.10) return null;

        const back = 1 / scale;
        const toFull = (p) => ({ x: p.x * back, y: p.y * back });
        return {
            tl: toFull(extrema.pMinSum),
            tr: toFull(extrema.pMaxDiff),
            br: toFull(extrema.pMaxSum),
            bl: toFull(extrema.pMinDiff)
        };
    }

    function defaultCorners(w, h) {
        const mx = w * 0.08, my = h * 0.08;
        return {
            tl: { x: mx, y: my }, tr: { x: w - mx, y: my },
            br: { x: w - mx, y: h - my }, bl: { x: mx, y: h - my }
        };
    }

    // Generic n-equation linear solver (Gaussian elimination, partial
    // pivoting) - used once below for the 8-unknown homography system.
    function solveLinearSystem(A, b) {
        const n = b.length;
        const M = A.map((row, i) => [...row, b[i]]);
        for (let col = 0; col < n; col++) {
            let maxRow = col;
            for (let r = col + 1; r < n; r++) {
                if (Math.abs(M[r][col]) > Math.abs(M[maxRow][col])) maxRow = r;
            }
            [M[col], M[maxRow]] = [M[maxRow], M[col]];
            if (Math.abs(M[col][col]) < 1e-9) return null;
            for (let r = 0; r < n; r++) {
                if (r === col) continue;
                const factor = M[r][col] / M[col][col];
                for (let c = col; c <= n; c++) M[r][c] -= factor * M[col][c];
            }
        }
        return M.map((row, i) => row[n] / row[i]);
    }

    // The projective transform (8 DOF, h33 fixed to 1) mapping the unit
    // square's 4 corners (0,0)-(1,0)-(1,1)-(0,1) onto quad.tl/tr/br/bl -
    // the standard "solve for the homography from 4 point correspondences"
    // linear system, derived from x=(h11 u+h12 v+h13)/(h31 u+h32 v+1) etc.
    function computeHomographyFromUnitSquare(quad) {
        const uv = [[0, 0], [1, 0], [1, 1], [0, 1]];
        const xy = [quad.tl, quad.tr, quad.br, quad.bl].map((p) => [p.x, p.y]);
        const A = [], b = [];
        for (let i = 0; i < 4; i++) {
            const [u, v] = uv[i];
            const [x, y] = xy[i];
            A.push([u, v, 1, 0, 0, 0, -u * x, -v * x]); b.push(x);
            A.push([0, 0, 0, u, v, 1, -u * y, -v * y]); b.push(y);
        }
        const h = solveLinearSystem(A, b);
        if (!h) return null;
        return { h11: h[0], h12: h[1], h13: h[2], h21: h[3], h22: h[4], h23: h[5], h31: h[6], h32: h[7] };
    }

    // Inverse-mapping warp: for every output pixel, find its source pixel
    // via the homography and bilinear-sample it - this is what actually
    // "straightens" a perspective-skewed photo into a flat, cropped
    // rectangle, not just an axis-aligned crop.
    function warpQuadToCanvas(sourceCanvas, quad, outW, outH) {
        const out = document.createElement('canvas');
        out.width = outW; out.height = outH;
        const octx = out.getContext('2d');

        const H = computeHomographyFromUnitSquare(quad);
        if (!H) {
            const xs = [quad.tl.x, quad.tr.x, quad.br.x, quad.bl.x];
            const ys = [quad.tl.y, quad.tr.y, quad.br.y, quad.bl.y];
            const minX = Math.min(...xs), maxX = Math.max(...xs);
            const minY = Math.min(...ys), maxY = Math.max(...ys);
            octx.drawImage(sourceCanvas, minX, minY, maxX - minX, maxY - minY, 0, 0, outW, outH);
            return out;
        }

        const sw = sourceCanvas.width, sh = sourceCanvas.height;
        const srcData = sourceCanvas.getContext('2d').getImageData(0, 0, sw, sh).data;
        const outData = octx.createImageData(outW, outH);

        for (let Y = 0; Y < outH; Y++) {
            const v = Y / outH;
            for (let X = 0; X < outW; X++) {
                const u = X / outW;
                const denom = H.h31 * u + H.h32 * v + 1;
                const sx = (H.h11 * u + H.h12 * v + H.h13) / denom;
                const sy = (H.h21 * u + H.h22 * v + H.h23) / denom;
                const outIdx = (Y * outW + X) * 4;

                if (sx < 0 || sy < 0 || sx >= sw - 1 || sy >= sh - 1) {
                    outData.data[outIdx + 3] = 0;
                    continue;
                }
                const x0 = Math.floor(sx), y0 = Math.floor(sy);
                const fx = sx - x0, fy = sy - y0;
                const x1 = Math.min(x0 + 1, sw - 1), y1 = Math.min(y0 + 1, sh - 1);
                for (let c = 0; c < 4; c++) {
                    const p00 = srcData[(y0 * sw + x0) * 4 + c];
                    const p10 = srcData[(y0 * sw + x1) * 4 + c];
                    const p01 = srcData[(y1 * sw + x0) * 4 + c];
                    const p11 = srcData[(y1 * sw + x1) * 4 + c];
                    const top = p00 + (p10 - p00) * fx;
                    const bottom = p01 + (p11 - p01) * fx;
                    outData.data[outIdx + c] = top + (bottom - top) * fy;
                }
                outData.data[outIdx + 3] = 255;
            }
        }
        octx.putImageData(outData, 0, 0);
        return out;
    }

    function distance(a, b) { return Math.hypot(a.x - b.x, a.y - b.y); }

    // Output size follows the quad's own average edge lengths (so a
    // portrait receipt stays portrait, a wide one stays wide), capped so
    // the exported JPEG stays a reasonable size.
    function pickOutputSize(quad) {
        const w = (distance(quad.tl, quad.tr) + distance(quad.bl, quad.br)) / 2;
        const h = (distance(quad.tl, quad.bl) + distance(quad.tr, quad.br)) / 2;
        const maxDim = 1400;
        const scale = Math.min(1, maxDim / Math.max(w, h, 1));
        return { outW: Math.max(1, Math.round(w * scale)), outH: Math.max(1, Math.round(h * scale)) };
    }

    // --- UI/camera orchestration ---

    let stream = null;
    let videoEl = null;
    let stillCanvas = null;
    let currentQuad = null;
    let dragCorner = null;
    let resolveOpen = null;

    function el(id) { return document.getElementById(id); }
    function backdrop() { return el('receipt-camera-modal-backdrop'); }

    function stopStream() {
        if (stream) { stream.getTracks().forEach((t) => t.stop()); stream = null; }
        if (videoEl) videoEl.srcObject = null;
    }

    function finish(file) {
        stopStream();
        backdrop().hidden = true;
        const resolve = resolveOpen;
        resolveOpen = null;
        if (resolve) resolve(file);
    }

    async function open() {
        return new Promise(async (resolve) => {
            resolveOpen = resolve;
            currentQuad = null;
            el('receipt-camera-status').textContent = 'Line the receipt up on a plain background, then tap Capture - the corners are detected automatically.';
            el('receipt-camera-live-pane').hidden = false;
            el('receipt-camera-crop-pane').hidden = true;
            el('receipt-camera-shoot-btn').hidden = false;
            backdrop().hidden = false;

            videoEl = el('receipt-camera-video');
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                el('receipt-camera-status').textContent = "This browser can't access the camera here - close this and use Choose File instead.";
                el('receipt-camera-shoot-btn').hidden = true;
                return;
            }
            try {
                stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
                videoEl.srcObject = stream;
            } catch (err) {
                el('receipt-camera-status').textContent = `Couldn't access the camera (${err.message || err.name || 'permission denied'}) - close this and use Choose File instead.`;
                el('receipt-camera-shoot-btn').hidden = true;
            }
        });
    }

    function capture(sourceEl, sourceW, sourceH) {
        stillCanvas = el('receipt-camera-still-canvas');
        stillCanvas.width = sourceW;
        stillCanvas.height = sourceH;
        stillCanvas.getContext('2d').drawImage(sourceEl, 0, 0, sourceW, sourceH);

        currentQuad = detectDocumentCorners(stillCanvas) || defaultCorners(sourceW, sourceH);

        el('receipt-camera-live-pane').hidden = true;
        el('receipt-camera-crop-pane').hidden = false;
        buildCropOverlay();
    }

    el('receipt-camera-shoot-btn').addEventListener('click', () => {
        if (!videoEl.videoWidth) return;
        capture(videoEl, videoEl.videoWidth, videoEl.videoHeight);
    });

    el('receipt-camera-retake-btn').addEventListener('click', () => {
        el('receipt-camera-crop-pane').hidden = true;
        el('receipt-camera-live-pane').hidden = false;
    });

    el('receipt-camera-close').addEventListener('click', () => finish(null));
    el('receipt-camera-cancel-btn').addEventListener('click', () => finish(null));
    backdrop().addEventListener('click', (e) => { if (e.target === backdrop()) finish(null); });

    // Built once per capture (viewBox set to the still's exact pixel size,
    // so handle/polygon coordinates ARE canvas pixel coordinates - no
    // percent-space conversion needed) - only updated in place afterward,
    // since a drag gesture holds pointer capture on one of these exact
    // handle elements and replacing them mid-drag (e.g. via innerHTML)
    // would silently break that gesture.
    function buildCropOverlay() {
        const svg = el('receipt-camera-crop-overlay');
        svg.setAttribute('viewBox', `0 0 ${stillCanvas.width} ${stillCanvas.height}`);
        svg.innerHTML = `
            <polygon class="receipt-crop-outline" id="receipt-crop-polygon"></polygon>
            <circle class="receipt-crop-handle" data-corner="tl" r="${stillCanvas.width * 0.018}"></circle>
            <circle class="receipt-crop-handle" data-corner="tr" r="${stillCanvas.width * 0.018}"></circle>
            <circle class="receipt-crop-handle" data-corner="br" r="${stillCanvas.width * 0.018}"></circle>
            <circle class="receipt-crop-handle" data-corner="bl" r="${stillCanvas.width * 0.018}"></circle>
        `;
        svg.querySelectorAll('.receipt-crop-handle').forEach((handle) => {
            handle.addEventListener('pointerdown', (e) => {
                e.preventDefault();
                handle.setPointerCapture(e.pointerId);
                dragCorner = handle.dataset.corner;
            });
            handle.addEventListener('pointermove', (e) => {
                if (dragCorner !== handle.dataset.corner) return;
                const rect = svg.getBoundingClientRect();
                const x = Math.min(stillCanvas.width, Math.max(0, ((e.clientX - rect.left) / rect.width) * stillCanvas.width));
                const y = Math.min(stillCanvas.height, Math.max(0, ((e.clientY - rect.top) / rect.height) * stillCanvas.height));
                currentQuad[dragCorner] = { x, y };
                updateCropOverlay();
            });
            handle.addEventListener('pointerup', (e) => { handle.releasePointerCapture(e.pointerId); dragCorner = null; });
        });
        updateCropOverlay();
    }

    function updateCropOverlay() {
        const svg = el('receipt-camera-crop-overlay');
        const pts = [currentQuad.tl, currentQuad.tr, currentQuad.br, currentQuad.bl];
        svg.querySelector('#receipt-crop-polygon').setAttribute('points', pts.map((p) => `${p.x},${p.y}`).join(' '));
        svg.querySelectorAll('.receipt-crop-handle').forEach((handle) => {
            const p = currentQuad[handle.dataset.corner];
            handle.setAttribute('cx', p.x);
            handle.setAttribute('cy', p.y);
        });
    }

    el('receipt-camera-use-btn').addEventListener('click', () => {
        const { outW, outH } = pickOutputSize(currentQuad);
        const out = warpQuadToCanvas(stillCanvas, currentQuad, outW, outH);
        out.toBlob((blob) => {
            if (!blob) { el('receipt-camera-status').textContent = 'Could not process that crop - try again.'; return; }
            finish(new File([blob], `receipt-${Date.now()}.jpg`, { type: 'image/jpeg' }));
        }, 'image/jpeg', 0.92);
    });

    return {
        open,
        // Exposed for the same reason window.openVideoViewer is - a small,
        // genuinely reusable piece of internals, and the only practical
        // way to exercise the detection/warp math against a fixed test
        // image rather than a live camera.
        _internal: { detectDocumentCorners, defaultCorners, warpQuadToCanvas, computeHomographyFromUnitSquare, capture, currentQuad: () => currentQuad }
    };
})();
