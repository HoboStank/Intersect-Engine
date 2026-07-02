# syntax=docker/dockerfile:1

# Multi-stage build: publish Intersect.Server for linux-x64
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything (repo is multi-project; solution restores all)
COPY . .

    # Restore and build server. Use build + copy of the bin output into /tmp/publish
    # to avoid dotnet publish copying satellite resource files (MSB3027 failures inside container).
    RUN dotnet restore Intersect.Server/Intersect.Server.csproj \
    && dotnet build Intersect.Server/Intersect.Server.csproj -c Release \
        && mkdir -p /tmp/publish \
        && cp -r Intersect.Server/bin/Release/net8.0/* /tmp/publish/ || true \
        # If build produced a linux-x64 folder, flatten it into /tmp/publish so runtime finds Intersect.Server.dll at /app
        && if [ -d /tmp/publish/linux-x64 ]; then \
            cp -r /tmp/publish/linux-x64/* /tmp/publish/ || true; \
            rm -rf /tmp/publish/linux-x64 || true; \
        fi \
        # Normalize artifact names: some build outputs use 'Intersect Server.*' (space) instead of 'Intersect.Server.*' (dot).
        && if [ -f /tmp/publish/'Intersect Server.dll' ]; then mv /tmp/publish/'Intersect Server.dll' /tmp/publish/'Intersect.Server.dll' || true; fi \
        && if [ -f /tmp/publish/'Intersect Server.deps.json' ]; then mv /tmp/publish/'Intersect Server.deps.json' /tmp/publish/'Intersect.Server.deps.json' || true; fi \
        && if [ -f /tmp/publish/'Intersect Server.runtimeconfig.json' ]; then mv /tmp/publish/'Intersect Server.runtimeconfig.json' /tmp/publish/'Intersect.Server.runtimeconfig.json' || true; fi \
        && if [ -f /tmp/publish/'Intersect Server.xml' ]; then mv /tmp/publish/'Intersect Server.xml' /tmp/publish/'Intersect.Server.xml' || true; fi \
        && if [ -f /tmp/publish/'Intersect Server.staticwebassets.runtime.json' ]; then mv /tmp/publish/'Intersect Server.staticwebassets.runtime.json' /tmp/publish/'Intersect.Server.staticwebassets.runtime.json' || true; fi \
        && if [ -f /tmp/publish/'Intersect Server' ]; then mv /tmp/publish/'Intersect Server' /tmp/publish/'Intersect.Server' || true; fi

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Persist game data and logs outside the container
VOLUME ["/app/resources", "/app/logs"]

# Copy published server
COPY --from=build /tmp/publish .

# Network: game port (UDP) and web UI/API (HTTP/HTTPS)
EXPOSE 5400/udp
EXPOSE 5400/tcp
EXPOSE 5443/tcp

# Default ASP.NET Core URL binding for the embedded web UI (dev HTTP)
ENV ASPNETCORE_URLS=http://0.0.0.0:5400

ENTRYPOINT ["dotnet", "Intersect.Server.dll"]
