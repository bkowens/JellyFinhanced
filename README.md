<h1 align="center">JellyFinhanced</h1>
<h3 align="center">Jellyfin — Enhanced with MySQL Support and Performance Improvements</h3>

---

<p align="center">
<img alt="Logo Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/banner-logo-solid.svg?sanitize=true"/>
<br/>
<br/>
<a href="https://github.com/jellyfin/jellyfin">
<img alt="GPL 2.0 License" src="https://img.shields.io/github/license/jellyfin/jellyfin.svg"/>
</a>
<a href="https://github.com/jellyfin/jellyfin/releases">
<img alt="Based on Jellyfin" src="https://img.shields.io/badge/based%20on-jellyfin%2010.12.0-blue.svg"/>
</a>
</p>

---

JellyFinhanced is a fork of [Jellyfin](https://github.com/jellyfin/jellyfin) — the free, open-source media server — extended with a MySQL/MariaDB database backend and a suite of parallelism and async performance improvements targeting high-concurrency deployments and large media libraries.

---

## What's Changed

### MySQL Database Backend

The default Jellyfin installation uses SQLite, which limits write concurrency and becomes a bottleneck on large libraries. JellyFinhanced adds a full **MySQL/MariaDB provider** built on [Pomelo.EntityFrameworkCore.MySql](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql), switchable via a single configuration file with no code changes required.

- EF Core migrations manage the full schema lifecycle
- Connection string and individual option formats both supported
- `utf8mb4` character set enforced for full Unicode compatibility
- Configurable locking behavior (`NoLock`, `Pessimistic`, `Optimistic`)
- Automatic schema migrations applied on startup
- Backup/restore workflow documented below

### Performance Improvements

Eight targeted parallelism improvements were implemented across the core media server pipeline. All parallel paths use explicit concurrency caps, `Interlocked` counters, and independent EF Core contexts per branch to prevent thread pool saturation and connection pool exhaustion.

#### HIGH Impact

| Area | Before | After | Estimated Gain |
|---|---|---|---|
| **People validation** (`PeopleValidator`) | Sequential per-person metadata refresh | `Parallel.ForEachAsync` capped at `min(CPU, 4)` | ~3.5x faster — 50-min task → ~14 min on large libraries |
| **Image downloads** (`LibraryManager`) | Sequential HTTP fetch per image type | `Task.WhenAll` + `SemaphoreSlim(4)` gate; CPU work (dimension/blurhash) stays serial | ~4x faster per-item image update |
| **Guide refresh** (`GuideManager`) | One channel at a time — 100 channels × 100ms = 10s serial | `Channel<T>` producer-consumer: 8-way parallel fetches, single serial DB writer | ~3.5x faster guide refresh (15s → ~4s) |
| **Live TV channel listing** (`LiveTvManager`) | `.GetAwaiter().GetResult()` on every channel-list request — thread pool starvation under load | `async Task AddChannelInfoAsync` propagated through `ILiveTvManager` and `DtoService` | Eliminates a blocked thread per concurrent TV guide request |

#### MEDIUM Impact

| Area | Before | After | Estimated Gain |
|---|---|---|---|
| **Remote metadata search** (`ProviderManager`) | TMDB, TVDB, MusicBrainz queried sequentially | `Task.WhenAll` fan-out; serial dedup after | ~4x lower search latency (4 providers × 300ms → 300ms) |
| **DTO construction** (`DtoService`) | 50-100 items built sequentially per browse page | `Task.WhenAll` + `SemaphoreSlim(4)` for non-IBN types; IBN path stays serial | 2-3x faster page load for I/O-heavy DTO fields |
| **Library root refresh** (`LibraryManager`) | Each top-level collection folder refreshed one at a time at startup | `Task.WhenAll` + `SemaphoreSlim(4)` | ~8x faster startup step (4s → ~0.5s) |
| **Metadata savers** (`ProviderManager`) | NFO, image, and XML savers run sequentially | `Task.WhenAll`; `DateLastSaved` set once after all complete | ~3x faster metadata write path |

#### Parallelism Prerequisites Applied Consistently

- All `Parallel.ForEachAsync` calls include explicit `MaxDegreeOfParallelism` + `CancellationToken`
- Counters in parallel loops use `Interlocked.Increment`
- Each parallel branch that touches the database uses its own `IDbContextFactory<T>.CreateDbContextAsync()` context
- `SemaphoreSlim` gates all `Task.WhenAll` paths accessing I/O or DB connections

### Extended Test Coverage

Additional unit and integration tests covering:

- `CleanDatabaseScheduledTask` — dead-item cleanup, progress reporting, cancellation
- `BaseItemRepository` — extended query and data integrity tests
- `SessionManager` — extended session lifecycle tests
- API controllers — `InstantMixController`, `LibraryController`, `MoviesController`, `PlaylistsController`, `SuggestionsController`, `TvShowsController`, `UserLibraryController`
- Integration tests — `ApiKeyController`, `ConfigurationController`, `DevicesController`, `FilterController`, `ImageController`, `SessionController`, `SystemController`, and others
- E2E and regression test suites

---

## MySQL Database Configuration

### 1. Create the MySQL Database and User

Connect to MySQL as root:

```bash
sudo mysql
```

```sql
CREATE DATABASE jellyfin CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'jellyfin'@'localhost' IDENTIFIED BY 'your_secure_password';
GRANT ALL PRIVILEGES ON jellyfin.* TO 'jellyfin'@'localhost';
FLUSH PRIVILEGES;
EXIT;
```

For a remote MySQL server, replace `'localhost'` with the Jellyfin server's IP address or `'%'` for any host.

### 2. Create `database.json`

Jellyfin reads database configuration from `database.json` in the configuration directory.

**Default config directory locations:**

| Condition | Path |
|---|---|
| `--configdir` CLI flag | Value of the flag |
| `$JELLYFIN_CONFIG_DIR` | Value of the variable |
| `--datadir` / `$JELLYFIN_DATA_DIR` set | `{datadir}/config` |
| Default (no XDG config) | `~/.local/share/jellyfin/config` |
| Default (XDG config exists) | `~/.config/jellyfin` |

Create the config directory and `database.json`:

```bash
mkdir -p ~/.local/share/jellyfin/config

cat > ~/.local/share/jellyfin/config/database.json << 'EOF'
{
  "DatabaseType": "Jellyfin-MySQL",
  "LockingBehavior": "NoLock",
  "CustomProviderOptions": {
    "ConnectionString": "Server=localhost;Port=3306;Database=jellyfin;User=jellyfin;Password=your_secure_password;SslMode=Preferred;CharSet=utf8mb4;"
  }
}
EOF
```

Replace `your_secure_password` with the password set in step 1.

### Connection String Options

| Option | Default | Description |
|---|---|---|
| `Server` | `localhost` | MySQL hostname or IP |
| `Port` | `3306` | MySQL port |
| `Database` | `jellyfin` | Database name |
| `User` | `jellyfin` | MySQL username |
| `Password` | *(empty)* | MySQL password |
| `SslMode` | `Preferred` | `None`, `Preferred`, or `Required` |
| `CharSet` | `utf8mb4` | Must be `utf8mb4` |

### Locking Behavior

| Value | Description |
|---|---|
| `NoLock` | No extra concurrency control — recommended for MySQL |
| `Pessimistic` | Database-level row locking |
| `Optimistic` | Row-version conflict detection |

---

## Deploying with MySQL

### Prerequisites

- **.NET 10 SDK**
- **MySQL 8.x or MariaDB 10.6+**
- **FFmpeg** (for transcoding)

#### Install .NET 10

```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0

# Add to ~/.bashrc for persistence
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
```

#### Install MySQL (Ubuntu/Debian)

```bash
sudo apt update
sudo apt install -y mysql-server
sudo systemctl enable --now mysql
```

Or MariaDB as a drop-in replacement:

```bash
sudo apt install -y mariadb-server
sudo systemctl enable --now mariadb
```

#### Install FFmpeg

```bash
sudo apt install -y ffmpeg
```

### Build from Source

```bash
git clone https://github.com/bkowens/JellyFinhanced.git
cd JellyFinhanced
dotnet build Jellyfin.sln -c Release
```

The server binary is output to `Jellyfin.Server/bin/Release/net10.0/`.

### Build the Web Client

```bash
cd jellyfin-web
npm install
npm run build
# Output is at jellyfin-web/dist/
```

### Configure MySQL

Follow the [MySQL Database Configuration](#mysql-database-configuration) steps above.

### Run the Server

#### Development

```bash
dotnet run --project Jellyfin.Server -- \
  --webdir /absolute/path/to/JellyFinhanced/jellyfin-web/dist
```

#### Production (compiled binary)

```bash
dotnet Jellyfin.Server/bin/Release/net10.0/jellyfin.dll \
  --datadir /srv/jellyfin/data \
  --configdir /etc/jellyfin \
  --cachedir /var/cache/jellyfin \
  --webdir /srv/jellyfin/web/dist
```

Place `database.json` at `/etc/jellyfin/database.json` when using `--configdir /etc/jellyfin`.

#### Environment Variables (alternative to CLI flags)

```bash
export JELLYFIN_DATA_DIR=/srv/jellyfin/data
export JELLYFIN_CONFIG_DIR=/etc/jellyfin
export JELLYFIN_CACHE_DIR=/var/cache/jellyfin
export JELLYFIN_LOG_DIR=/var/log/jellyfin

dotnet Jellyfin.Server/bin/Release/net10.0/jellyfin.dll \
  --webdir /srv/jellyfin/web/dist
```

### Running as a systemd Service

Create `/etc/systemd/system/jellyfin.service`:

```ini
[Unit]
Description=JellyFinhanced Media Server
After=network.target mysql.service
Requires=mysql.service

[Service]
Type=simple
User=jellyfin
Group=jellyfin
Environment=JELLYFIN_DATA_DIR=/var/lib/jellyfin
Environment=JELLYFIN_CONFIG_DIR=/etc/jellyfin
Environment=JELLYFIN_CACHE_DIR=/var/cache/jellyfin
Environment=JELLYFIN_LOG_DIR=/var/log/jellyfin
ExecStart=/usr/bin/dotnet /opt/jellyfin/jellyfin.dll \
  --webdir /opt/jellyfin/web/dist
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Create the service user and directories:

```bash
sudo useradd --system --no-create-home --shell /sbin/nologin jellyfin
sudo mkdir -p /var/lib/jellyfin /etc/jellyfin /var/cache/jellyfin /var/log/jellyfin
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin /etc/jellyfin /var/cache/jellyfin /var/log/jellyfin
```

Place `database.json` at `/etc/jellyfin/database.json`, then enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now jellyfin
sudo systemctl status jellyfin
```

### Verify the Deployment

On first start, Jellyfin applies EF Core migrations to create the MySQL schema. Check logs:

```bash
sudo journalctl -u jellyfin -f
```

Look for:

```
MySQL connection string: Server=localhost;Port=3306;Database=jellyfin;...
```

Verify tables were created:

```bash
mysql -u jellyfin -p jellyfin -e "SHOW TABLES;"
```

Access the setup wizard at `http://localhost:8096`.

### Backup and Restore

**Manual backup:**

```bash
mysqldump -u jellyfin -p jellyfin > jellyfin_backup_$(date +%Y%m%d).sql
```

**Restore:**

```bash
mysql -u jellyfin -p jellyfin < jellyfin_backup_20260308.sql
```

Automatic migration-safety backups are written to `{DataPath}/MySqlBackups/{timestamp}/` as JSON files per table.

### Troubleshooting

**Connection refused:**
```bash
sudo systemctl status mysql
mysql -u jellyfin -p -e "SELECT 1;"
```

**Access denied:**
```bash
sudo mysql -e "SHOW GRANTS FOR 'jellyfin'@'localhost';"
```

**Character set errors** — ensure the database was created with `utf8mb4`:
```bash
sudo mysql -e "SELECT DEFAULT_CHARACTER_SET_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='jellyfin';"
```

---

## Running the Tests

```bash
dotnet test Jellyfin.sln
```

To exclude the two known pre-existing network resolution failures:

```bash
dotnet test Jellyfin.sln --filter "FullyQualifiedName!~ParseNetworkTests"
```

---

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet)
- [FFmpeg](https://ffmpeg.org/)
- MySQL 8.x or MariaDB 10.6+ (for MySQL backend)

### Clone and Build

```bash
git clone https://github.com/bkowens/JellyFinhanced.git
cd JellyFinhanced
dotnet build Jellyfin.sln
```

### Run from the Command Line

```bash
dotnet run --project Jellyfin.Server -- \
  --webdir /absolute/path/to/jellyfin-web/dist
```

Default port is `18096`. Access the server at `http://localhost:18096`.

API documentation: `http://localhost:18096/api-docs/swagger/index.html`

### IDE

Any IDE with .NET 10 support works. Visual Studio 2022+ and VS Code (with the C# extension) are recommended. Press `F5` to run with the debugger attached.

---

## Upstream

JellyFinhanced is based on [Jellyfin](https://github.com/jellyfin/jellyfin) 10.12.0. Jellyfin is free software released under the [GNU GPL v2](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html).

For upstream documentation, issue tracking, and community support, see [jellyfin.org](https://jellyfin.org).

---

<p align="center">
This project is supported by:
<br/>
<br/>
<a href="https://www.digitalocean.com"><img src="https://opensource.nyc3.cdn.digitaloceanspaces.com/attribution/assets/SVG/DO_Logo_horizontal_blue.svg" height="50px" alt="DigitalOcean"></a>
    &nbsp;
<a href="https://www.jetbrains.com"><img src="https://gist.githubusercontent.com/anthonylavado/e8b2403deee9581e0b4cb8cd675af7db/raw/fa104b7d73f759d7262794b94569f1b89df41c0b/jetbrains.svg" height="50px" alt="JetBrains logo"></a>
</p>
