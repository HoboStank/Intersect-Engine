# Intersect Engine – AI Agent Reference

This guide gives AI agents a compact, accurate map of the Intersect repo: how the server boots, how networking and plugins work, and how to run it reliably in Docker.

## Quick map of the solution
- Entry points
  - Server: `Intersect.Server/Program.cs` → `Bootstrapper.Start(...)`
  - Client: `Intersect.Client/Program.cs` → `Intersect.Client.Core/Program.Main(...)`
  - Editor: `Intersect.Editor/`
- Core server namespaces
  - Context: `Intersect.Server.Core/Core/*` (bootstrap, services, lifecycle)
  - Networking: `Intersect.Server.Core/Networking/**` (LiteNetLib UDP on 5400)
  - Web: `Intersect.Server/Web/**` (Kestrel hosting, REST/API, certs)
  - Database: `Intersect.Server.Core/Database/**` (SQLite by default)
  - Plugins: `Intersect (Core)/Plugins/**`, `Intersect.Server.*.Plugins/**`
- Build: .NET 8 (TFM net8.0) via `Directory.Build.props` and `Intersect.props`

## Server startup flow (end-to-end)
- `Program.Main(args)` wires factories and calls `Bootstrapper.Start(assembly, args, ExtractWwwroot)`.
- `Bootstrapper.Start(...)` steps:
  1) Parse CLI args into `ServerCommandLineOptions`.
  2) `PreContextSetup`:
     - Load `Strings` and `Options` from `resources/config.json`.
     - Ensure notification templates and output dirs exist.
     - Export platform native dependency for SQLite (`e_sqlite3`).
  3) Set up logging and packet type/handler registries.
  4) Create `ServerContext` (or `FullServerContext` via `ServerContextFactory`).
  5) `PostContextSetup`:
     - Initialize databases and load game data.
     - Print entity counts, cache game data packet.
  6) Start main action queue (`context.StartWithActionQueue()` loop).
  7) On exit, optionally wait on console or halt on error.

Key classes involved:
- `ServerCommandLineOptions` (`Intersect.Server.Core/Core/ServerCommandLineOptions.cs`):
  - `--no-console` disable interactive console thread
  - `--no-upnp` disable NAT/UPnP
  - `--no-port-check` disable external port check
  - `--port <n>` to override `Options.Instance.ServerPort`
- `ServerContext` (`Intersect.Server.Core/Core/ServerContext.cs`):
  - Creates and starts networking; owns services and shutdown sequencing.
- `FullServerContext` (`Intersect.Server/Core/FullServerContext.cs`):
  - Extends `ServerContext` with REST API and network checks (UPnP/port checker).

## Networking
- Transport: LiteNetLib UDP on `Options.Instance.ServerPort` (default 5400).
- Factory wiring in `Program.Main`: `ServerContext.NetworkFactory => new ServerNetwork(...)` with `NetworkConfiguration`.
- Startup message: "Server started. Using UDP Port #<port>" when `Listen()` succeeds.
- UPnP/Port check:
  - Controlled by config (`OpenPortChecker`, `UPnP`) and CLI flags (`--no-port-check`, `--no-upnp`).
  - Runs in `FullServerContext.CheckNetwork()`; safe to disable inside containers.

## Interactive console behavior
- Console thread lives in `Intersect.Server/Core/ConsoleService.*`.
- If stdin is closed (common in containers), `Console.ReadLine()` returns null and the server requests shutdown.
- Solution: run with `--no-console` to avoid starting the console thread.

## Docker reference
- Files under `docker/`:
  - `server/Dockerfile`: multi-stage build (SDK → ASP.NET runtime). Uses `dotnet build` + copy of bin to avoid rare publish copy errors.
  - `docker-compose.yml`: maps volumes for `/app/resources` and `/app/logs`; exposes 5400 UDP/TCP and 5443 TCP; sets `ASPNETCORE_URLS` to `http://0.0.0.0:5400`.
- Recommended compose command:
  - `--no-console --no-upnp --no-port-check`
- Volumes:
  - Mount host `docker/server-data/resources` to `/app/resources` for configs, data, and migrations.
  - Mount host `docker/server-data/logs` to `/app/logs`.
- Confirmed runtime logs:
  - Kestrel bound to HTTP 5400 and HTTPS 5443
  - UDP game port listening on 5400

## Configuration & data
- Options: `resources/config.json` loaded by `Options.LoadFromDisk()` during pre-context setup.
- Databases: default SQLite files are created under the resources path unless configured otherwise.
- Metrics/logging: controlled by `Options.Instance.Metrics` and `Options.Logging` settings.

## Plugins
- Contracts live under `Intersect (Core)/Plugins/**`.
- Server plugin context and service lifecycle integrated in `ServerContext`.
- Examples under `Examples/Intersect.Examples.Plugin.Server/*` show packet handlers and hooks.

## Common flows for agents
- Login/registration/logout:
  - Packets handled in `Intersect.Server.Core/Networking/PacketHandler.cs` and responses via `PacketSender.cs`.
  - Users/players live under `Intersect.Server.Core/Database/PlayerData/*`.
- Game data cache:
  - `PacketSender.CacheGameDataPacket()` after data load so clients can fetch efficiently.
- Shutdown:
  - `ServerContext.Dispose` gracefully disconnects clients, persists guilds/users, and exits via `Environment.Exit`.

## Cross-platform build notes
- Non-Windows builds may exclude Windows-only projects. A helper patch `disable-windows-only.patch` is present for CI/Linux/macOS.

## Operational tips
- If the server exits immediately in Docker, ensure `--no-console` is set.
- UPnP will fail in bridge networks; disable with `--no-upnp` and rely on port mappings.
- To change game port, either set it in `resources/config.json` or pass `--port <n>`; update compose port mappings accordingly.
- For HTTPS, a dev cert is embedded; browsers may warn due to missing SAN. Production deployments should supply real certs.

## Repository directory map (quick reference)
Below are the top-level folders and a one-line purpose to help agents navigate the codebase quickly.

- `Intersect.Server/` — Server host project and HTTP/Kestrel entry point.
- `Intersect.Server.Core/` — Core server logic, contexts, lifecycle, services and networking glue.
- `Intersect.Client/` — Thin client entry; boots the MonoGame client.
- `Intersect.Client.Core/` — Main client runtime: UI, rendering, networking client, and content loading.
- `Intersect.Client.Framework/` — Shared client framework (graphics, Gwen skin, content manager).
- `Intersect.Editor/` — Game editor (WinForms + MonoGame). Windows-only utilities live here.
- `Intersect.Network/` — Networking abstractions and LiteNetLib implementation.
- `Intersect (Core)/` — Core libraries, data models, enums, utilities and plugin contracts.
- `Intersect.Tests/` & `Intersect.Tests.*/` — Unit and integration tests for client/server components.
- `Examples/` — Example plugins and sample code demonstrating plugin patterns and packet handlers.
- `docker/` — Dockerfile and docker-compose.yml used for building and running the server in containers.
- `Documentation/` — Docs, guides, and notes (including this AI-Agent reference).
- `scripts/`, `targets/` — Build and CI helper scripts and MSBuild targets.

## UI customization (skin and textures)
The client UI uses a Gwen-based skin implemented in `Intersect.Client.Framework/Gwen/Skin/IntersectSkin.cs`. Key facts and actionable steps:

- Texture name and disk-first lookup:
  - The skin expects a texture named `skin-intersect.png`.
  - At runtime `IntersectSkin` calls `contentManager.GetTexture(TextureType.Gui, "skin-intersect.png")` and will use any file present on disk before falling back to the embedded resource shipped with the assembly.

- Where to put an override (developer & modder instructions):
  1. Locate your client content directory (the client mounts or reads resources from a `resources` folder near the client executable). Typical paths used by the build/runtime are `resources/` in the client working directory or embedded plugin content for packaged builds.
  2. Place your replacement PNG at `resources/interface/skin-intersect.png` or the GUI folder the `GameContentManager` scans for `TextureType.Gui`. If uncertain, search for other GUI textures (e.g., `transtile.png`) in the `resources` tree to confirm the client content layout.
  3. Start the client. The `GameContentManager` will detect the on-disk `skin-intersect.png` and log "skin-intersect.png was found on disk, not using embedded version" if the logger is enabled.

- Changing colors and texture layout:
  - The skin samples colors and texture regions by reading pixels and slicing the `skin-intersect.png` (see `Renderer.PixelColor` and many `new Single(_texture, x, y, w, h)` / `new Bordered(...)` calls in `IntersectSkin.cs`). To change UI colors or button geometry, edit the PNG accordingly (maintain the existing layout of regions or update the skin code to reference new coordinates).

- Programmatic overrides and advanced theming:
  - If you want multiple skins or to switch skins at runtime, use the `IntersectSkin(Renderer.Base, GameContentManager, string textureName)` constructor overload to pass an alternative texture name — this requires a small client code change where the skin is constructed (search for `new IntersectSkin(` in the client startup code).
  - For plugin-provided skins, use `GameContentManager.LoadEmbedded` to load resources from plugins and call the skin constructor with that embedded texture name.

- Quick verification steps (smoke test):
  1. Stop the client (if running) and put your custom `skin-intersect.png` in the client `resources/interface/` (or `resources` top-level interface folder used by your build).
  2. Start the client. Check logs for the debug message indicating a disk-based skin was used.
  3. If the UI looks incorrect, either your PNG layout doesn't match the expected texture atlas or you need to edit `IntersectSkin.cs` to point to the right coordinates for elements you changed.

## Where to look next (useful files)
- `Intersect.Client.Framework/File_Management/GameContentManager.*` — content searching/loading logic and the TextureType enum.
- `Intersect.Client.Framework/Gwen/Skin/IntersectSkin.cs` — skin implementation (colors, texture slices, constructor overload to supply a custom name).
- `Intersect.Client.Core/Interface/Menu/LoginWindow.cs` — example of UI construction and where fonts/textures are requested.
- `Examples/Intersect.Examples.Plugin.Client/` — example plugin showing how to embed content in plugins.

---

I'll mark the UI-doc todo completed next and then we can run the client to verify Online state if you'd like.

