# Running Intersect from Source

This guide explains how to run the Intersect Engine client and server from source after building.

## Prerequisites

1. .NET 8 SDK installed
2. Git submodules initialized (`git submodule update --init --recursive`)
3. **Intersect-Assets** repository cloned (see below)

## Required Assets

The client and server need the official game assets to run properly. Without these, the client will show a black screen with minimal UI.

### One-time Setup: Clone and Copy Assets

```powershell
# From the Intersect-Engine repository root:

# 1. Clone the assets repository (main_full branch has compiled assets)
git clone --depth 1 --branch main_full https://github.com/AscensionGameDev/Intersect-Assets.git temp-assets

# 2. Copy resources to client output
Copy-Item -Path "temp-assets\resources" -Destination "Intersect.Client\bin\Debug\net8.0\" -Recurse -Force

# 3. Copy resources to server output
Copy-Item -Path "temp-assets\resources" -Destination "Intersect.Server\bin\Debug\net8.0\" -Recurse -Force

# 4. (Optional) Clean up the temporary clone
Remove-Item -Path "temp-assets" -Recurse -Force
```

**Note:** You need to repeat step 2-3 after each clean build that deletes the `bin` directories.

## Building

```powershell
# Build in Debug configuration
dotnet build Intersect.sln -c Debug

# Or build in Release configuration
dotnet build Intersect.sln -c Release
```

## Running the Server

```powershell
# From the repository root:
dotnet run --project Intersect.Server --configuration Debug --no-build
```

The server will:
- Start on port 5400 (UDP for game, HTTP for REST API)
- Start on port 5443 (HTTPS for REST API)
- Create a SQLite database in `resources` if one doesn't exist
- Generate a default class and empty map if the database is empty

**Common Warnings (Safe to Ignore):**
- UPnP initialization failed — This is normal on many networks; local connections will work fine
- Plugin directory doesn't exist — Create `resources\plugins` if you want to load plugins

## Running the Client

```powershell
# From the repository root:
dotnet run --project Intersect.Client --configuration Debug --no-build
```

The client will:
- Connect to `localhost:5400` by default
- Load UI and graphics from the `resources` folder
- Show the login/character creation screen

**Troubleshooting:**
- **Black screen or minimal UI:** Resources folder is missing or incomplete. Follow the asset setup steps above.
- **"graphics-error.txt" created:** OpenGL/graphics driver issue. Check the file for details.
- **"audio-error.txt" created:** Audio device issue. The client will still run but without sound.

## Running Both (Server + Client)

Open two terminal windows:

**Terminal 1 (Server):**
```powershell
dotnet run --project Intersect.Server --configuration Debug --no-build
```

**Terminal 2 (Client):**
```powershell
# Wait a few seconds for server to start, then:
dotnet run --project Intersect.Client --configuration Debug --no-build
```

## Editor (Windows Only)

```powershell
dotnet run --project Intersect.Editor --configuration Debug --no-build
```

The editor is a Windows-only WinForms application for creating game content (maps, NPCs, items, etc.).

## Testing Your Skin Changes

After editing UI skin code in `Intersect.Client.Framework/Gwen/Skin/IntersectSkin.cs`:

1. Rebuild the solution: `dotnet build Intersect.sln -c Debug`
2. Ensure resources are copied (see asset setup above)
3. Restart the server and client
4. Check the UI in-game to see your changes

## Publishing for Distribution

For single-file executables:

```powershell
# Windows 64-bit
dotnet publish Intersect.Server -c Release -r win-x64

dotnet publish Intersect.Client -c Release -r win-x64

# Linux 64-bit
dotnet publish Intersect.Server -c Release -r linux-x64

dotnet publish Intersect.Client -c Release -r linux-x64

# macOS 64-bit
dotnet publish Intersect.Server -c Release -r osx-x64

dotnet publish Intersect.Client -c Release -r osx-x64
```

Published files will be in `bin\Release\net8.0\<runtime-id>\publish\`.

**Important:** Copy the `resources` folder to the publish directory before distributing.

## Additional Resources

- [Official Documentation](https://docs.freemmorpgmaker.com)
- [Intersect-Assets Repository](https://github.com/AscensionGameDev/Intersect-Assets)
- [Community Forums](https://ascensiongamedev.com)
