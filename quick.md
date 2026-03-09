# Quick Start

Run Jellyfin from the project root after building.

## Build

```bash
dotnet build Jellyfin.sln -c Release
```

## Run

Without the web client (API only):

```bash
dotnet run --project Jellyfin.Server -- --nowebclient
```

With the web client (requires [jellyfin-web](https://github.com/jellyfin/jellyfin-web) built alongside):

```bash
dotnet run --project Jellyfin.Server -- --webdir /path/to/jellyfin-web/dist
```

Or run the compiled binary directly:

```bash
dotnet Jellyfin.Server/bin/Release/net10.0/jellyfin.dll --webdir /path/to/jellyfin-web/dist
```

The server starts at `http://localhost:8096`. By default Jellyfin looks for the web client at `jellyfin-web/` next to the binary. Use `--webdir` to point to a different location, or `--nowebclient` to skip hosting the web UI entirely.

## Common Options

| Flag | Description |
|---|---|
| `-d`, `--datadir <path>` | Data directory (database, backups, trickplay) |
| `-c`, `--configdir <path>` | Config directory (`database.json`, `system.xml`) |
| `-C`, `--cachedir <path>` | Cache directory (images, transcodes) |
| `-l`, `--logdir <path>` | Log file directory |
| `-w`, `--webdir <path>` | Path to jellyfin-web UI resources |
| `--ffmpeg <path>` | Path to FFmpeg binary |
| `--nowebclient` | Start without hosting the web UI |
| `--service` | Run as a headless service |

## Example with Custom Paths

```bash
dotnet run --project Jellyfin.Server -- \
  --datadir ./data \
  --configdir ./config \
  --cachedir ./cache \
  --nowebclient
```
