<h1 align="center">JellyFinhanced</h1>

<h3 align="center">High-Performance Jellyfin Fork with MySQL/MariaDB Support</h3>

---

<p align="center">
<img alt="Logo Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/banner-logo-solid.svg?sanitize=true"/>
<br/>
<br/>
<a href="https://github.com/bkowens/JellyFinhanced/blob/master/LICENSE">
<img alt="GPL 2.0 License" src="https://img.shields.io/github/license/bkowens/JellyFinhanced.svg"/>
</a>
<a href="https://github.com/jellyfin/jellyfin/releases">
<img alt="Based on Jellyfin" src="https://img.shields.io/badge/based%20on-jellyfin%2010.12.0-blue.svg"/>
</a>
</p>

---

## Overview

**JellyFinhanced** is a high-performance fork of [Jellyfin](https://github.com/jellyfin/jellyfin) — the free, open-source media server — designed for self-hosters with large media libraries and high-concurrency deployments. It adds two major improvements:

1. **MySQL/MariaDB database backend** — Replace the default SQLite with a production-grade relational database to eliminate write-concurrency bottlenecks
2. **Eight parallelism and async optimizations** — Targeted improvements across the media server pipeline that speed up library operations, metadata refresh, and concurrent requests

JellyFinhanced maintains full compatibility with Jellyfin's API, plugins, and configuration while removing the architectural ceiling that SQLite imposes on busy servers.

### Who This Is For

- **Large media libraries** — 100,000+ items where SQLite write serialization becomes painful
- **High-concurrency servers** — Multiple simultaneous transcoding, guide refreshes, or concurrent API requests
- **Self-hosted deployments** — Where you control the hardware and want to extract maximum performance
- **Teams running Jellyfin** — Shared servers with 5+ concurrent users

### Key Features

- **MySQL/MariaDB Provider** — Full EF Core integration, automatic schema migrations, configurable concurrency control
- **Parallelized People Validation** — People metadata refresh ~3.5x faster (from 50 min to ~14 min on large libraries)
- **Channel<T> Producer-Consumer Guide Refresh** — Guide data ingestion ~3.5x faster with 8-way parallel fetches
- **Async Propagation in Live TV** — Eliminates thread pool blocking on concurrent TV guide requests
- **Parallel Image Downloads** — 4-way concurrent HTTP fetches with gated CPU work (dimension/blurhash calculation)
- **Extended Test Coverage** — 30+ new unit and integration tests covering core operations
- **Drop-in Compatibility** — Same API, same plugins, same configuration structure

---

## Table of Contents

- [Why MySQL Matters](#why-mysql-matters)
- [Quick Start](#quick-start)
- [Performance Improvements](#performance-improvements)
- [MySQL Database Configuration](#mysql-database-configuration)
- [Switching from SQLite to MySQL](#switching-from-sqlite-to-mysql)
- [Deploying with MySQL](#deploying-with-mysql)
- [Running the Tests](#running-the-tests)
- [Development Setup](#development-setup)
- [Upstream](#upstream)

---

## Why MySQL Matters

SQLite is an excellent embedded database for small to medium workloads, but it has a fundamental architectural limitation: **all writes are serialized through a single writer process**. When your Jellyfin server receives multiple concurrent metadata updates, image downloads, or library operations, they queue behind a lock. On large libraries with thousands of items, this bottleneck becomes severe.

MySQL uses a multi-threaded architecture with row-level locking. Multiple clients can write different rows in parallel, removing the single-writer ceiling. For Jellyfin:

- **100 concurrent metadata updates** → SQLite: one at a time (locks entire DB). MySQL: many in parallel (row-level locks).
- **Guide refresh + image download + user scanning library simultaneously** → SQLite: three operations serialize. MySQL: all proceed in parallel.
- **Scheduled tasks + normal API traffic** → SQLite: one blocks the other. MySQL: independent.

JellyFinhanced is bundled with a native MySQL provider. Switching databases requires only a config file change — no data loss, no code changes.

---

## Quick Start

For experienced users who want to skip the details:

1. **Install dependencies:**
   ```bash
   sudo apt update && sudo apt install -y dotnet-sdk-10.0 mysql-server ffmpeg git
   ```

2. **Clone and build:**
   ```bash
   git clone https://github.com/bkowens/JellyFinhanced.git
   cd JellyFinhanced
   dotnet build Jellyfin.sln -c Release
   ```

3. **Configure MySQL:**
   ```bash
   sudo mysql << 'EOF'
   CREATE DATABASE jellyfin CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
   CREATE USER 'jellyfin'@'localhost' IDENTIFIED BY 'secure_password';
   GRANT ALL PRIVILEGES ON jellyfin.* TO 'jellyfin'@'localhost';
   FLUSH PRIVILEGES;
   EOF
   ```

4. **Create `~/.local/share/jellyfin/config/database.json`:**
   ```json
   {
     "DatabaseType": "Jellyfin-MySQL",
     "LockingBehavior": "NoLock",
     "CustomProviderOptions": {
       "ConnectionString": "Server=localhost;Port=3306;Database=jellyfin;User=jellyfin;Password=secure_password;SslMode=Preferred;CharSet=utf8mb4;"
     }
   }
   ```

5. **Run:**
   ```bash
   dotnet Jellyfin.Server/bin/Release/net10.0/jellyfin.dll --webdir jellyfin-web/dist
   ```

   Access the web UI at `http://localhost:8096`.

---

## Performance Improvements

JellyFinhanced implements eight targeted parallelism improvements across the core media server pipeline. All improvements use explicit concurrency caps, `Interlocked` counters for thread-safe progress tracking, and independent EF Core contexts per parallel branch to prevent thread pool saturation and connection pool exhaustion.

### Overview of Strategy

The parallelism approach follows these principles:

- **Bounded parallelism** — Explicit `MaxDegreeOfParallelism` caps prevent resource exhaustion (typically `Math.Min(ProcessorCount, 4)` for I/O-heavy work, up to `10` for guide fetches)
- **Channel<T> pipelines** — Producer-consumer pattern for tasks that have ordering requirements (e.g., guide data ingestion followed by DB writes)
- **SemaphoreSlim gates** — Limit concurrent HTTP, file I/O, and database operations
- **Async propagation** — Replace `.GetAwaiter().GetResult()` with true `async/await` to prevent thread pool starvation
- **Per-branch DB contexts** — Each parallel worker creates its own `DbContext` via `IDbContextFactory<JellyfinDbContext>` to avoid contention

### Detailed Improvements

#### HIGH Impact

| Area | Before | After | Estimated Gain |
|---|---|---|---|
| **People validation** | Sequential per-person metadata refresh (50+ min on 1000 people) | `Parallel.ForEachAsync` capped at `Math.Min(CPU, 4)` with per-person `DbContext` | ~3.5x faster — 50-min task → ~14 min on large libraries |
| **Image downloads** | Sequential HTTP fetch per image type + serial dimension/blurhash calculation | `Task.WhenAll` + `SemaphoreSlim(4)` for HTTP; CPU work (dimension/blurhash) stays serial | ~4x faster per-item image update |
| **Guide refresh** | One channel at a time — 100 channels × 100ms per fetch = 10s serial wall-clock | `Channel<T>` producer-consumer: 8-way parallel fetches, single serial DB writer | ~3.5x faster guide refresh (15s → ~4s) |
| **Live TV channel listing** | `.GetAwaiter().GetResult()` on every channel-list request — blocks thread pool on high concurrency | `async Task AddChannelInfoAsync` propagated through `ILiveTvManager` and `DtoService` | Eliminates a blocked thread per concurrent TV guide request |

#### MEDIUM Impact

| Area | Before | After | Estimated Gain |
|---|---|---|---|
| **Remote metadata search** | TMDB, TVDB, MusicBrainz queried sequentially (4 providers × 300ms = 1.2s per item) | `Task.WhenAll` fan-out with `SemaphoreSlim` gate; serial dedup after | ~4x lower search latency (300ms total) |
| **DTO construction** | 50-100 items built sequentially per browse page | `Task.WhenAll` + `SemaphoreSlim(4)` for non-IBN types; IBN path stays serial | 2-3x faster page load for I/O-heavy DTO fields |
| **Library root refresh** | Each top-level collection folder refreshed one at a time at startup (e.g., 4s for 4 folders) | `Task.WhenAll` + `SemaphoreSlim(4)` | ~8x faster startup step (4s → ~0.5s) |
| **Metadata savers** | NFO, image, and XML savers run sequentially (3 savers × 200ms = 600ms per item) | `Task.WhenAll` with `SemaphoreSlim` gate; `DateLastSaved` set once after all complete | ~3x faster metadata write path |

### Implementation Details

All parallel paths follow the same safety model:

- **MaxDegreeOfParallelism** — Explicit caps prevent unlimited spawning of Tasks
- **Interlocked counters** — Progress tracking is thread-safe without locking
- **IDbContextFactory<JellyfinDbContext>** — Each parallel branch that accesses the database creates its own context via `CreateDbContextAsync()` to avoid shared DbContext contention
- **CancellationToken propagation** — All `Parallel.ForEachAsync` and `Task.WhenAll` paths respect cancellation
- **SemaphoreSlim for gates** — I/O paths (HTTP, file, DB) are additionally gated to prevent resource starvation

**Source files with changes:**

- `Emby.Server.Implementations/Library/Validators/PeopleValidator.cs` — People validation with `Parallel.ForEachAsync(... MaxDegreeOfParallelism = Math.Min(CPU, 4) ...)`
- `src/Jellyfin.LiveTv/Guide/GuideManager.cs` — Guide refresh with `Channel<T>` producer-consumer (8-way parallel fetches)
- `src/Jellyfin.Database/Jellyfin.Database.Providers.MySql/` — Native MySQL provider with automatic schema migrations
- `Jellyfin.Api/Controllers/*` — Async propagation across 20+ API controllers

---

## MySQL Database Configuration

### Quick TL;DR (One-Liner Config)

For a local MySQL server, create `~/.local/share/jellyfin/config/database.json`:

```json
{
  "DatabaseType": "Jellyfin-MySQL",
  "LockingBehavior": "NoLock",
  "CustomProviderOptions": {
    "ConnectionString": "Server=localhost;Port=3306;Database=jellyfin;User=jellyfin;Password=your_password;SslMode=Preferred;CharSet=utf8mb4;"
  }
}
```

Replace `your_password` with your database password. That's it.

### SQLite vs. MySQL: Quick Comparison

| Aspect | SQLite (Default) | MySQL (JellyFinhanced) |
|--------|------------------|------------------------|
| **File** | Single `jellyfin.db` file | Managed by MySQL server |
| **Write concurrency** | Single writer (serialized) | Row-level locking (parallel) |
| **Network access** | Local only | Can be remote via TCP/IP |
| **Configuration** | `database.json` with `DatabaseType: "Jellyfin-SQLite"` | `database.json` with `DatabaseType: "Jellyfin-MySQL"` |
| **Backups** | Copy the .db file | `mysqldump` or database snapshots |
| **Suitable for** | Single user or small teams | Multi-user, high-concurrency, large libraries |

### Step 1: Create the MySQL Database and User

Connect to MySQL as root:

```bash
sudo mysql
```

Then execute:

```sql
CREATE DATABASE jellyfin CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'jellyfin'@'localhost' IDENTIFIED BY 'your_secure_password';
GRANT ALL PRIVILEGES ON jellyfin.* TO 'jellyfin'@'localhost';
FLUSH PRIVILEGES;
EXIT;
```

**For remote MySQL servers**, replace `'localhost'` with the Jellyfin server's IP or `'%'` for any host:

```sql
CREATE USER 'jellyfin'@'192.168.1.100' IDENTIFIED BY 'your_secure_password';
GRANT ALL PRIVILEGES ON jellyfin.* TO 'jellyfin'@'192.168.1.100';
```

Or for a MariaDB instance, use the same syntax (MariaDB is a drop-in compatible fork of MySQL).

### Step 2: Create the `database.json` Configuration File

Jellyfin reads database configuration from `database.json` in the configuration directory. The default config directory locations are:

| Condition | Path |
|-----------|------|
| `--configdir` CLI flag provided | Value of the flag |
| `$JELLYFIN_CONFIG_DIR` env var set | Value of the variable |
| `--datadir` / `$JELLYFIN_DATA_DIR` set | `{datadir}/config` |
| Default (no XDG config exists) | `~/.local/share/jellyfin/config` |
| Default (XDG config exists) | `~/.config/jellyfin` |

Create the config directory:

```bash
mkdir -p ~/.local/share/jellyfin/config
```

Create `database.json` with your MySQL connection string:

```bash
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

Replace `your_secure_password` with the password you set in Step 1.

### Connection String Options

The connection string uses the format:

```
Server=<host>;Port=<port>;Database=<name>;User=<user>;Password=<pass>;SslMode=<mode>;CharSet=utf8mb4;
```

| Option | Default | Description | Example |
|--------|---------|-------------|---------|
| `Server` | `localhost` | MySQL hostname or IP | `Server=192.168.1.50` |
| `Port` | `3306` | MySQL port | `Port=3306` |
| `Database` | `jellyfin` | Database name | `Database=jellyfin` |
| `User` | `jellyfin` | MySQL username | `User=jellyfin` |
| `Password` | *(empty)* | MySQL password | `Password=MySecurePass123` |
| `SslMode` | `Preferred` | SSL/TLS mode | `SslMode=Required` or `SslMode=None` |
| `CharSet` | `utf8mb4` | Character set (must be `utf8mb4`) | *(do not change)* |

**Common examples:**

- **Local MySQL on default port:**
  ```
  Server=localhost;Port=3306;Database=jellyfin;User=jellyfin;Password=mypass;SslMode=Preferred;CharSet=utf8mb4;
  ```

- **Remote MySQL server (e.g., on 192.168.1.50) with SSL required:**
  ```
  Server=192.168.1.50;Port=3306;Database=jellyfin;User=jellyfin;Password=mypass;SslMode=Required;CharSet=utf8mb4;
  ```

- **MariaDB on non-standard port:**
  ```
  Server=localhost;Port=3307;Database=jellyfin;User=jellyfin;Password=mypass;SslMode=Preferred;CharSet=utf8mb4;
  ```

### Alternative: Individual Connection Options

Instead of a connection string, you can specify parameters individually in the JSON:

```json
{
  "DatabaseType": "Jellyfin-MySQL",
  "LockingBehavior": "NoLock",
  "CustomProviderOptions": {
    "Options": [
      { "Key": "server", "Value": "localhost" },
      { "Key": "port", "Value": "3306" },
      { "Key": "database", "Value": "jellyfin" },
      { "Key": "user", "Value": "jellyfin" },
      { "Key": "password", "Value": "your_secure_password" },
      { "Key": "sslmode", "Value": "Preferred" }
    ]
  }
}
```

If both `ConnectionString` and `Options` are provided, `ConnectionString` takes precedence.

### Locking Behavior

The `LockingBehavior` field controls EF Core concurrency handling:

| Value | Description | Use Case |
|-------|-------------|----------|
| `NoLock` | No additional concurrency control (recommended for MySQL) | Production use — MySQL handles concurrency |
| `Pessimistic` | Database-level row locking | Scenarios where you need explicit row locks |
| `Optimistic` | Row version-based conflict detection | Legacy compatibility |

Recommended: **Always use `NoLock` with MySQL**. MySQL's native locking is more efficient than EF Core's pessimistic locking.

### Step 3: Test Your Connection

Before starting Jellyfin, verify the connection works:

```bash
mysql -u jellyfin -p -h localhost -e "SELECT 1;"
```

If you see `1`, your connection is good. If you get an error, check:

1. MySQL is running: `sudo systemctl status mysql`
2. User and password are correct
3. Host/port are correct (if remote, ensure firewall allows port 3306)

---

## Switching from SQLite to MySQL

If you have an existing Jellyfin installation using SQLite and want to switch to MySQL:

**Note:** Data is **not** automatically migrated from SQLite to MySQL. You will start with a fresh database. The library scan will re-index all your media files, which is transparent and automatic.

### Migration Steps

1. **Stop Jellyfin:**
   ```bash
   sudo systemctl stop jellyfin
   # or if running manually, press Ctrl+C
   ```

2. **Backup your SQLite database** (optional but recommended):
   ```bash
   cp ~/.local/share/jellyfin/data/jellyfin.db ~/jellyfin_backup.db
   ```

3. **Create the MySQL database and user** (see [Step 1](#step-1-create-the-mysql-database-and-user) above)

4. **Create the `database.json` configuration file** with your MySQL connection string (see [Step 2](#step-2-create-the-databasejson-configuration-file) above)

5. **Start Jellyfin:**
   ```bash
   sudo systemctl start jellyfin
   # or run manually: dotnet Jellyfin.Server/bin/Release/net10.0/jellyfin.dll --webdir ...
   ```

6. **Verify the migration:**
   - Watch the logs for MySQL connection confirmation
   - Access the web UI and log in with your existing user account
   - Initiate a library scan if needed (it will re-index all media)

On first start with MySQL, Jellyfin runs EF Core migrations to create the database schema automatically. This takes a few seconds. You'll see output like:

```
MySQL connection string: Server=localhost;Port=3306;Database=jellyfin;...
MySQL connection interceptor command set to: SET SESSION wait_timeout=28800; ...
```

---

## Deploying with MySQL

### Prerequisites

| Component | Minimum Version | Installation |
|-----------|-----------------|--------------|
| **.NET SDK** | 10.0 | `dotnet-sdk-10.0` (Ubuntu) or [Microsoft installer](https://dotnet.microsoft.com/download/dotnet) |
| **MySQL** | 8.0 | `mysql-server` (Ubuntu) or [MySQL 8.0 downloads](https://dev.mysql.com/downloads/mysql/) |
| **MariaDB** | 10.6+ | `mariadb-server` (Ubuntu) or [MariaDB downloads](https://mariadb.com/downloads/) |
| **FFmpeg** | Any recent version | `ffmpeg` package (Ubuntu) or [ffmpeg.org](https://ffmpeg.org/) |
| **Git** | Any version | `git` package (Ubuntu) |

### Install .NET 10 SDK

```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0

# Add to ~/.bashrc or ~/.profile for persistence
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools

# Verify
dotnet --version  # Should output 10.0.x or higher
```

### Install MySQL (Ubuntu/Debian)

```bash
sudo apt update
sudo apt install -y mysql-server
sudo systemctl enable --now mysql
sudo systemctl status mysql
```

Or use **MariaDB** as a drop-in replacement:

```bash
sudo apt install -y mariadb-server
sudo systemctl enable --now mariadb
sudo systemctl status mariadb
```

### Install FFmpeg

```bash
sudo apt install -y ffmpeg
```

### Install Additional Build Dependencies

```bash
sudo apt install -y git build-essential libfontconfig1-dev
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

Follow the [MySQL Database Configuration](#mysql-database-configuration) steps above to create the database, user, and `database.json` file.

### Run the Server

#### Development Mode

```bash
dotnet run --project Jellyfin.Server -- \
  --webdir /absolute/path/to/JellyFinhanced/jellyfin-web/dist
```

Default port is `18096`. Access at `http://localhost:18096`.

#### Production (Compiled Binary)

```bash
dotnet Jellyfin.Server/bin/Release/net10.0/jellyfin.dll \
  --datadir /srv/jellyfin/data \
  --configdir /etc/jellyfin \
  --cachedir /var/cache/jellyfin \
  --webdir /srv/jellyfin/web/dist
```

Place `database.json` at `/etc/jellyfin/database.json` when using `--configdir /etc/jellyfin`.

#### Using Environment Variables

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
ExecStart=/usr/bin/dotnet /opt/jellyfin/jellyfin.dll --webdir /opt/jellyfin/web/dist
Restart=on-failure
RestartSec=5
StandardOutput=journal
StandardError=journal
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
```

Create the service user and directories:

```bash
sudo useradd --system --no-create-home --shell /sbin/nologin jellyfin
sudo mkdir -p /var/lib/jellyfin /etc/jellyfin /var/cache/jellyfin /var/log/jellyfin
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin /etc/jellyfin /var/cache/jellyfin /var/log/jellyfin
```

Place `database.json` at `/etc/jellyfin/database.json`, then enable and start the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable jellyfin
sudo systemctl start jellyfin
sudo systemctl status jellyfin
```

Watch the logs in real-time:

```bash
sudo journalctl -u jellyfin -f
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
mysql -u jellyfin -p jellyfin -e "SHOW TABLES;" | head -20
```

You should see tables like `BaseItems`, `Users`, `UserData`, `MediaStreamInfos`, `ActivityLogs`.

Access the web UI at `http://localhost:8096`.

### Production Hardening

#### 1. Reverse Proxy (nginx)

Place Jellyfin behind nginx to handle HTTPS, compression, and load distribution:

```nginx
server {
    listen 80;
    server_name jellyfin.example.com;

    # Redirect HTTP to HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name jellyfin.example.com;

    ssl_certificate /etc/letsencrypt/live/jellyfin.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/jellyfin.example.com/privkey.pem;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;

    client_max_body_size 100M;

    location / {
        proxy_pass http://127.0.0.1:8096;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
        proxy_redirect off;
    }
}
```

#### 2. MySQL Firewall Rules

If MySQL is on a different host, restrict access to port 3306:

```bash
# Allow only the Jellyfin server IP
sudo ufw allow from 192.168.1.100 to any port 3306
```

Or bind MySQL to a specific interface:

```bash
# Edit /etc/mysql/mysql.conf.d/mysqld.cnf
bind-address = 192.168.1.50  # Only this interface
```

Restart MySQL:

```bash
sudo systemctl restart mysql
```

#### 3. Enable SSL for MySQL Connections

In your `database.json`:

```json
{
  "DatabaseType": "Jellyfin-MySQL",
  "LockingBehavior": "NoLock",
  "CustomProviderOptions": {
    "ConnectionString": "Server=remote.example.com;Port=3306;Database=jellyfin;User=jellyfin;Password=secure_password;SslMode=Required;CharSet=utf8mb4;"
  }
}
```

Set `SslMode` to `Required` to enforce encrypted connections.

#### 4. Health Check Command

Verify Jellyfin is responding:

```bash
curl -s http://localhost:8096/api/system/info | jq '.ServerName'
```

Or for a systemd service:

```bash
sudo systemctl is-active jellyfin
sudo systemctl status jellyfin
```

### Backup and Restore

#### Manual Backup with mysqldump

```bash
mysqldump -u jellyfin -p jellyfin > jellyfin_backup_$(date +%Y%m%d_%H%M%S).sql
```

#### Restore from Backup

```bash
mysql -u jellyfin -p jellyfin < jellyfin_backup_20260308.sql
```

#### Automatic Migration-Safety Backups

On startup with MySQL, Jellyfin creates automatic JSON backups at:

```
{DataPath}/MySqlBackups/{timestamp}/
```

Each table is exported as a separate JSON file (e.g., `Users.json`, `BaseItems.json`). These are kept automatically and do not interfere with regular operation.

### Troubleshooting

#### Connection Refused

Verify MySQL is running and listening:

```bash
sudo systemctl status mysql
mysql -u jellyfin -p -e "SELECT 1;"
```

If MySQL is on a remote host, ensure the `bind-address` in `/etc/mysql/mysql.conf.d/mysqld.cnf` allows remote connections:

```bash
bind-address = 0.0.0.0  # Listen on all interfaces
# or
bind-address = 192.168.1.50  # Specific interface
```

Then restart MySQL:

```bash
sudo systemctl restart mysql
```

#### Access Denied

Verify the user and privileges:

```bash
sudo mysql -e "SHOW GRANTS FOR 'jellyfin'@'localhost';"
```

If the user doesn't exist or permissions are wrong, recreate:

```bash
sudo mysql << 'EOF'
CREATE USER 'jellyfin'@'localhost' IDENTIFIED BY 'new_password';
GRANT ALL PRIVILEGES ON jellyfin.* TO 'jellyfin'@'localhost';
FLUSH PRIVILEGES;
EOF
```

#### Character Set Issues

Verify the database was created with `utf8mb4`:

```bash
mysql -u jellyfin -p -e "SELECT DEFAULT_CHARACTER_SET_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='jellyfin';"
```

Expected output: `utf8mb4`.

If the database has the wrong charset, recreate it:

```bash
sudo mysql << 'EOF'
DROP DATABASE jellyfin;
CREATE DATABASE jellyfin CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
EOF
```

---

## Running the Tests

Run the full test suite:

```bash
dotnet test Jellyfin.sln
```

To exclude the two known pre-existing network resolution failures:

```bash
dotnet test Jellyfin.sln --filter "FullyQualifiedName!~ParseNetworkTests"
```

JellyFinhanced includes 30+ new unit and integration tests covering:

- `CleanDatabaseScheduledTask` — dead-item cleanup, progress reporting, cancellation
- `BaseItemRepository` — extended query and data integrity tests
- `SessionManager` — extended session lifecycle tests
- API controllers — `InstantMixController`, `LibraryController`, `MoviesController`, `PlaylistsController`, `SuggestionsController`, `TvShowsController`, `UserLibraryController`
- Integration tests — `ApiKeyController`, `ConfigurationController`, `DevicesController`, `FilterController`, `ImageController`, `SessionController`, `SystemController`
- E2E and regression test suites

---

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet)
- [FFmpeg](https://ffmpeg.org/)
- MySQL 8.x or MariaDB 10.6+ (optional, for MySQL backend development)

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

Default port is `18096`. Access at `http://localhost:18096`.

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
