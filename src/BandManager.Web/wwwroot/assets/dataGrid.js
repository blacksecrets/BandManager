// Shared sortable/paged/searchable grid - generalizes the pattern
// catalog.js's Details view has used for a while (10/page, debounced
// search, click-to-sort headers) into something every other "make this
// a grid" page can call instead of hand-rolling its own copy. Adds
// optional row checkboxes + header select-all + bulk-action buttons and
// an optional footer row, neither of which catalog.js needed.
//
// Usage: const grid = DataGrid.render(containerEl, { columns, rows, ... });
// grid.setRows(newRows) swaps the data (e.g. after a save) without
// losing the current search/sort/page state. See dataGrid.css for the
// matching styles - link it once per host page.
(function () {
    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str ?? '';
        return div.innerHTML;
    }

    const PAGE_SIZE_DEFAULT = 10;

    window.DataGrid = {
        /**
         * @param {HTMLElement} container
         * @param {{
         *   columns: Array<{
         *     key: string, label: string, sortable?: boolean,
         *     render?: (row: any) => string,
         *     sortValue?: (row: any) => (string|number),
         *     searchValue?: (row: any) => string,
         *     searchable?: boolean, className?: string
         *   }>,
         *   rows: any[],
         *   getRowId?: (row: any) => string,
         *   pageSize?: number,
         *   searchable?: boolean,
         *   searchPlaceholder?: string,
         *   checkboxes?: boolean,
         *   bulkActions?: Array<{ label: string, onClick: (ids: string[]) => (void|Promise<void>) }>,
         *   rowClassName?: (row: any) => string,
         *   onRowClick?: (row: any) => void,
         *   emptyMessage?: string,
         *   defaultSortKey?: string,
         *   defaultSortDir?: 'asc'|'desc',
         *   renderFooter?: (filteredRows: any[]) => string
         * }} options
         */
        render(container, options) {
            const {
                columns,
                getRowId = (row) => row.id,
                pageSize = PAGE_SIZE_DEFAULT,
                searchable = true,
                searchPlaceholder = 'Search...',
                checkboxes = false,
                bulkActions = [],
                rowClassName = null,
                onRowClick = null,
                emptyMessage = 'Nothing here yet.',
                defaultSortKey = null,
                defaultSortDir = 'asc',
                renderFooter = null
            } = options;

            let rows = options.rows || [];
            let searchQuery = '';
            let sortKey = defaultSortKey;
            let sortDir = defaultSortDir;
            let currentPage = 1;
            const selectedIds = new Set();

            container.innerHTML = `
                <div class="data-grid">
                    ${searchable ? `
                        <div class="data-grid-toolbar">
                            <input type="search" class="data-grid-search" placeholder="${escapeHtml(searchPlaceholder)}">
                            ${bulkActions.map((a, i) => `<button type="button" class="data-grid-bulk-btn" data-action-index="${i}" disabled>${escapeHtml(a.label)}</button>`).join('')}
                        </div>
                    ` : (bulkActions.length ? `
                        <div class="data-grid-toolbar">
                            ${bulkActions.map((a, i) => `<button type="button" class="data-grid-bulk-btn" data-action-index="${i}" disabled>${escapeHtml(a.label)}</button>`).join('')}
                        </div>
                    ` : '')}
                    <div class="data-grid-table-wrap">
                        <table class="data-grid-table">
                            <thead><tr></tr></thead>
                            <tbody></tbody>
                            <tfoot></tfoot>
                        </table>
                    </div>
                    <div class="data-grid-pagination"></div>
                </div>
            `;

            const searchInput = container.querySelector('.data-grid-search');
            const theadRow = container.querySelector('thead tr');
            const tbody = container.querySelector('tbody');
            const tfoot = container.querySelector('tfoot');
            const paginationBox = container.querySelector('.data-grid-pagination');
            const bulkBtns = [...container.querySelectorAll('.data-grid-bulk-btn')];

            function matchesSearch(row) {
                if (!searchQuery) return true;
                const q = searchQuery.toLowerCase();
                return columns.some((col) => {
                    if (col.searchable === false) return false;
                    const value = col.searchValue ? col.searchValue(row) : (col.render ? col.render(row) : row[col.key]);
                    return String(value ?? '').toLowerCase().includes(q);
                });
            }

            function filteredRows() {
                return searchable && searchQuery ? rows.filter(matchesSearch) : rows.slice();
            }

            function sortedRows(list) {
                if (!sortKey) return list;
                const col = columns.find((c) => c.key === sortKey);
                const dir = sortDir === 'asc' ? 1 : -1;
                const valueOf = col && col.sortValue ? col.sortValue : (row) => row[sortKey];
                return [...list].sort((a, b) => {
                    const av = valueOf(a);
                    const bv = valueOf(b);
                    if (av == null && bv == null) return 0;
                    if (av == null) return dir;
                    if (bv == null) return -dir;
                    if (typeof av === 'number' && typeof bv === 'number') return dir * (av - bv);
                    return dir * String(av).localeCompare(String(bv));
                });
            }

            function updateBulkButtons() {
                bulkBtns.forEach((btn) => { btn.disabled = selectedIds.size === 0; });
            }

            function renderHeader() {
                theadRow.innerHTML = `
                    ${checkboxes ? '<th class="data-grid-check-col"><input type="checkbox" class="data-grid-select-all"></th>' : ''}
                    ${columns.map((col) => {
                        if (col.sortable === false) return `<th class="${col.className || ''}">${escapeHtml(col.label)}</th>`;
                        const active = sortKey === col.key;
                        const arrow = active ? (sortDir === 'asc' ? ' ▲' : ' ▼') : '';
                        return `<th class="data-grid-sortable ${active ? 'sorted' : ''} ${col.className || ''}" data-sort-key="${col.key}">${escapeHtml(col.label)}${arrow}</th>`;
                    }).join('')}
                `;
                theadRow.querySelectorAll('th[data-sort-key]').forEach((th) => {
                    th.addEventListener('click', () => {
                        const key = th.dataset.sortKey;
                        if (sortKey === key) sortDir = sortDir === 'asc' ? 'desc' : 'asc';
                        else { sortKey = key; sortDir = 'asc'; }
                        currentPage = 1;
                        renderAll();
                    });
                });
                const selectAll = theadRow.querySelector('.data-grid-select-all');
                if (selectAll) {
                    selectAll.addEventListener('change', () => {
                        const pageIds = currentPageRows.map(getRowId);
                        if (selectAll.checked) pageIds.forEach((id) => selectedIds.add(id));
                        else pageIds.forEach((id) => selectedIds.delete(id));
                        renderAll();
                    });
                }
            }

            let currentPageRows = [];

            function renderAll() {
                renderHeader();

                const filtered = filteredRows();
                const sorted = sortedRows(filtered);
                const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
                if (currentPage > totalPages) currentPage = totalPages;
                currentPageRows = sorted.slice((currentPage - 1) * pageSize, currentPage * pageSize);

                tbody.innerHTML = '';
                if (currentPageRows.length === 0) {
                    const colspan = columns.length + (checkboxes ? 1 : 0);
                    tbody.innerHTML = `<tr><td colspan="${colspan}" class="data-grid-empty">${escapeHtml(emptyMessage)}</td></tr>`;
                } else {
                    for (const row of currentPageRows) {
                        const id = getRowId(row);
                        const tr = document.createElement('tr');
                        tr.className = 'data-grid-row' + (rowClassName ? ` ${rowClassName(row) || ''}` : '');
                        tr.innerHTML = `
                            ${checkboxes ? `<td class="data-grid-check-col"><input type="checkbox" class="data-grid-row-check" ${selectedIds.has(id) ? 'checked' : ''}></td>` : ''}
                            ${columns.map((col) => `<td class="${col.className || ''}">${col.render ? col.render(row) : escapeHtml(row[col.key] ?? '')}</td>`).join('')}
                        `;
                        if (checkboxes) {
                            const cb = tr.querySelector('.data-grid-row-check');
                            cb.addEventListener('click', (e) => e.stopPropagation());
                            cb.addEventListener('change', () => {
                                if (cb.checked) selectedIds.add(id);
                                else selectedIds.delete(id);
                                updateBulkButtons();
                                const selectAll = theadRow.querySelector('.data-grid-select-all');
                                if (selectAll) selectAll.checked = currentPageRows.length > 0 && currentPageRows.every((r) => selectedIds.has(getRowId(r)));
                            });
                        }
                        if (onRowClick) {
                            tr.classList.add('data-grid-row-clickable');
                            tr.addEventListener('click', () => onRowClick(row));
                        }
                        tbody.appendChild(tr);
                    }
                }

                const selectAll = theadRow.querySelector('.data-grid-select-all');
                if (selectAll) selectAll.checked = currentPageRows.length > 0 && currentPageRows.every((r) => selectedIds.has(getRowId(r)));
                updateBulkButtons();

                tfoot.innerHTML = renderFooter ? renderFooter(filtered) : '';

                paginationBox.innerHTML = totalPages <= 1 ? '' : `
                    <button type="button" class="data-grid-page-prev" ${currentPage <= 1 ? 'disabled' : ''}>&laquo; Prev</button>
                    <span class="data-grid-page-status">Page ${currentPage} of ${totalPages}</span>
                    <button type="button" class="data-grid-page-next" ${currentPage >= totalPages ? 'disabled' : ''}>Next &raquo;</button>
                `;
                const prevBtn = paginationBox.querySelector('.data-grid-page-prev');
                const nextBtn = paginationBox.querySelector('.data-grid-page-next');
                if (prevBtn) prevBtn.addEventListener('click', () => { currentPage--; renderAll(); });
                if (nextBtn) nextBtn.addEventListener('click', () => { currentPage++; renderAll(); });
            }

            if (searchInput) {
                let searchTimer = null;
                searchInput.addEventListener('input', () => {
                    clearTimeout(searchTimer);
                    searchTimer = setTimeout(() => {
                        searchQuery = searchInput.value.trim();
                        currentPage = 1;
                        renderAll();
                    }, 250);
                });
            }

            bulkBtns.forEach((btn) => {
                btn.addEventListener('click', async () => {
                    const action = bulkActions[Number(btn.dataset.actionIndex)];
                    if (!action || selectedIds.size === 0) return;
                    await action.onClick([...selectedIds]);
                });
            });

            renderAll();

            return {
                setRows(newRows) {
                    rows = newRows || [];
                    // Drop selections for ids that no longer exist, so a
                    // bulk action's own refresh doesn't leave a stale
                    // "N selected" state pointing at rows that are gone.
                    const stillPresent = new Set(rows.map(getRowId));
                    [...selectedIds].forEach((id) => { if (!stillPresent.has(id)) selectedIds.delete(id); });
                    renderAll();
                },
                getSelectedIds() { return [...selectedIds]; },
                clearSelection() { selectedIds.clear(); renderAll(); }
            };
        }
    };
})();
