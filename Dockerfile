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

# data/ is where the credential key, uploads, catalog files, and flyer
# cache live - must be a mounted volume (see docker-compose.yml) so it
# survives a container rebuild/redeploy.
RUN mkdir -p /app/data && useradd --uid 5000 --create-home bandmanager \
    && chown -R bandmanager:bandmanager /app
USER bandmanager

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BandManager.Web.dll"]
