FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BandManager.slnx .
COPY src/BandManager.Data/BandManager.Data.csproj src/BandManager.Data/
COPY src/BandManager.Web/BandManager.Web.csproj src/BandManager.Web/
COPY src/BandManager.Migrate/BandManager.Migrate.csproj src/BandManager.Migrate/
RUN dotnet restore src/BandManager.Web/BandManager.Web.csproj

COPY src/ src/
RUN dotnet publish src/BandManager.Web/BandManager.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Tech Rider PDF export (TechRiderPdfService) needs a real headless
# Chromium - installed here, as root, before the image drops to its
# non-root user below. PLAYWRIGHT_BROWSERS_PATH is set to a folder under
# /app (rather than the default ~/.cache/ms-playwright under /root) so it
# lands somewhere the non-root bandmanager user below can actually reach -
# /root itself isn't traversable by another user. The Microsoft.Playwright
# package only generates a PowerShell installer script (playwright.ps1),
# so pwsh is installed just long enough to run it; it's not a runtime
# dependency of the app or of Chromium itself, so it's left in place
# rather than risking an apt-get autoremove pulling out a shared lib
# Chromium also needs.
ENV PLAYWRIGHT_BROWSERS_PATH=/app/.playwright-browsers
RUN apt-get update && apt-get install -y --no-install-recommends wget ca-certificates \
    && wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && apt-get update && apt-get install -y --no-install-recommends powershell \
    && rm -rf /var/lib/apt/lists/* \
    && pwsh /app/playwright.ps1 install --with-deps chromium

# data/ is where the credential key, uploads, catalog files, and flyer
# cache live - must be a mounted volume (see docker-compose.yml) so it
# survives a container rebuild/redeploy.
RUN mkdir -p /app/data && useradd --uid 5000 --create-home bandmanager \
    && chown -R bandmanager:bandmanager /app
USER bandmanager

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BandManager.Web.dll"]
