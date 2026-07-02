# Intersect Engine – AI agent guide

Use these project-specific notes to be productive fast. Keep actions aligned with the files and patterns below.

## Big picture
- Solution `Intersect.sln` includes: Client (MonoGame), Server (headless .NET host), Editor (WinForms+MonoGame, Windows-only), Core libs, Network (LiteNetLib), and Frameworks.
- Entry points: `Intersect.Server/Program.cs`; `Intersect.Client/Program.cs` → `Intersect.Client.Core/Program.Main(...)`; Editor under `Intersect.Editor/`.
- Networking: Abstraction in `Intersect.Network` with LiteNetLib impl (`Intersect.Network/LiteNetLib/**`). Server bootstraps `ServerNetwork` via `ServerContext.NetworkFactory`; handshake uses generated RSA/AES keys.
- Plugins: First-class across client/server/editor. Core contracts in `Intersect (Core)/Plugins/**`, server contexts in `Intersect.Server.Core/Plugins/**`. Working examples under `Examples/Intersect.Examples.Plugin*`.

## Build, run, test
- Prereq: .NET 8 SDK. After clone run submodules; on non-Windows apply `disable-windows-only.patch` (CI does this on Linux/macOS).
- Build: `dotnet build Intersect.sln -c Debug` for dev; publish single-file with `dotnet publish -c Release -r win-x64|linux-x64|osx-x64`.
- Keys: Building `Intersect.Network` creates versioned handshake keys in `Intersect.Network/bin/<Config>/keys`; ensure it builds before publishing Server.
- Run: Server via `Intersect.Server.csproj`; Client via `Intersect.Client.csproj` (use `--debugger` to wait for debugger); Editor runs only on Windows.
- Tests: Use configuration `DebugTests` (e.g., run tests on `Intersect.sln` with `-c DebugTests`).

### Configs & migrations
- Database settings (DatabaseType, ConnectionString) are read by `Intersect.Server/Program.cs` in `CreateHostBuilder(...)` for EF design-time operations; set them in the server config used at design-time.
- `CreateHostBuilder` wires DB provider via `DatabaseType` and `ConnectionString` to generate migrations; ensure these are valid before invoking EF tools.
- Server port comes from `Options.Instance.ServerPort` (read by `NetworkConfiguration`); change via server configuration rather than hardcoding.

## Patterns & conventions
- Global MSBuild: `Directory.Build.props` + `Intersect.props` (TFM net8.0, ImplicitUsings, Nullable, multi-RIDs; build embeds BuildNumber/CommitSha).
- Solution configs: `Debug`, `Release`, plus `DebugTests`, `DebugFull`, `DebugPlugins` for targeted work.
- Client runtime exports native deps at startup (see `ExportDependencies()` in `Intersect.Client.Core/Program.cs`); graphics/audio failures write `graphics-error.txt` / `audio-error.txt` and open help links.
- Common args: `--plugin-directory <path>` to load built plugins from output folders.

## Plugins (follow these examples)
- Packet handler: `Examples/Intersect.Examples.Plugin.Server/Networking/Handlers/ExamplePluginClientPacketHandler.cs`.
- Hook: `Examples/Intersect.Examples.Plugin.Server/Networking/Hooks/ExamplePluginLoginPostHook.cs`.
- Manifest/entry: `Examples/Intersect.Examples.Plugin*/Manifest.cs`, `*PluginEntry.cs`.

## Server web features
- `Intersect.Server/Program.cs` extracts embedded `wwwroot` on first run. Dev homepage http://localhost:5400; published HTTPS defaults to 5443.
- Avatar site graphics require following `Documentation/AvatarController/AvatarController.md` (manual asset step).

## CI signals
- `.github/workflows/*.yml` build on push/PR, generate keys, publish for RIDs, and package via `.github/bundles/*.json`. Main workflows skip `Intersect.Tests*` for build speed.

## Pointers
- Architecture & features: `README.md`, `REQUIREMENTS.md`, `Documentation/Features.md`.
- Build wiring: `Directory.Build.props`, `Common.props`, `Intersect.props`, `targets/*.targets`.
- Networking: `Intersect.Network/**`, `Intersect.Server/Networking/**`.
- Plugin surface: `Intersect (Core)/Plugins/**`, `Intersect.Server.*.Plugins/**`, `Examples/**`.
