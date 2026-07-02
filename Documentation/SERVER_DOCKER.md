# Intersect Server in Docker

This guide runs the server in a Linux container to avoid local environment issues (UPnP, ports, deps). It builds from source, publishes the server, and mounts resources/logs outside the container.

## Prereqs
- Windows 10/11 with Docker Desktop installed and running
- .NET 8 SDK already used for local builds (optional for Docker-only flow)

## Build the image
From the repo root:

```powershell
# Build the server image
docker build -f docker/server/Dockerfile -t intersect-server:local .
```

## First run (create persistent folders)
```powershell
# Create data folders on host for resources and logs
mkdir docker\server-data\resources -Force | Out-Null
mkdir docker\server-data\logs -Force | Out-Null

# Seed a Docker-friendly server config (UPnP off)
Copy-Item docker\server\config.docker.json docker\server-data\resources\config.json -Force
```

If you have the Intersect-Assets repository, copy the `resources` content (maps, items, etc.) into `docker/server-data/resources`.

## Run with Docker Compose
```powershell
# From repo root
cd docker
# Build and start
docker compose up --build -d
# Tail server log
docker logs -f intersect-server
```

Exposed ports:
- 5400/udp: Game networking
- 5400/tcp: Web UI (dev HTTP)
- 5443/tcp: Web UI (HTTPS in published builds)

If you see UPnP warnings locally, they're harmless in Docker (UPnP is disabled in the Docker config).

## Using your host assets
Place your game data under `docker/server-data/resources`. The container mounts it at `/app/resources`.

## Stop and remove
```powershell
cd docker
docker compose down
```

## Troubleshooting
- If the client shows Offline, ensure the server container is running and listening on 5400/udp. On the same host, the client should connect to `127.0.0.1`.
- If you use a remote client, forward UDP 5400 in your firewall/router. UPnP is disabled in Docker; do manual forwarding if needed.
- SQLite DB files will be created inside `docker/server-data/resources`.