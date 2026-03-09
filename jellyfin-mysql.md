# Jellyfin with MySQL on Ubuntu 24.04

This guide covers installing all dependencies, building Jellyfin from source, configuring it to use MySQL/MariaDB, and running the server from the command line on Ubuntu 24.04.

---

## Prerequisites

### .NET 10 SDK

Jellyfin targets `net10.0`. Install the .NET 10 SDK from Microsoft's package feed:

```bash
# Install the Microsoft package signing key and feed
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0

# Add to PATH (add this to ~/.bashrc or ~/.profile for persistence)
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
```

Verify the installation:

```bash
dotnet --version
# Should output 10.0.x
```

### MySQL Server

Install MySQL 8.x from the Ubuntu repositories:

```bash
sudo apt update
sudo apt install -y mysql-server
```

Start and enable the service:

```bash
sudo systemctl start mysql
sudo systemctl enable mysql
```

Verify MySQL is running:

```bash
sudo systemctl status mysql
```

Alternatively, **MariaDB** can be used as a drop-in replacement:

```bash
sudo apt install -y mariadb-server
sudo systemctl start mariadb
sudo systemctl enable mariadb
```

### FFmpeg

Jellyfin requires FFmpeg for media transcoding:

```bash
sudo apt install -y ffmpeg
```

### Additional Build Dependencies

```bash
sudo apt install -y git build-essential libfontconfig1-dev
```

---

## Build Jellyfin from Source

Clone the repository and build:

```bash
git clone https://github.com/jellyfin/jellyfin.git
cd jellyfin
dotnet build Jellyfin.sln
```

The server executable is built to `Jellyfin.Server/bin/Debug/net10.0/jellyfin`.

For a release build:

```bash
dotnet build Jellyfin.sln -c Release
```

---

## Configure MySQL

### Create the Database and User

Connect to MySQL as root and create a dedicated database and user:

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

For remote MySQL servers, replace `'localhost'` with the Jellyfin host address or `'%'` for any host.

### Create the database.json Configuration File

Jellyfin reads its database configuration from `database.json` in the configuration directory.

The default configuration directory on Linux is:

| Condition | Path |
|---|---|
| `$JELLYFIN_CONFIG_DIR` is set | `$JELLYFIN_CONFIG_DIR` |
| `--configdir` CLI flag is used | Value of the flag |
| `--datadir` or `$JELLYFIN_DATA_DIR` is set | `{datadir}/config` |
| Default (no XDG config exists) | `~/.local/share/jellyfin/config` |
| Default (XDG config exists) | `~/.config/jellyfin` |

Create the configuration directory if it doesn't exist:

```bash
mkdir -p ~/.local/share/jellyfin/config
```

Create `database.json` with the MySQL connection string:

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

Replace `your_secure_password` with the password you set when creating the MySQL user.

### Connection String Format

The full connection string format is:

```
Server=<host>;Port=<port>;Database=<name>;User=<user>;Password=<pass>;SslMode=<mode>;CharSet=utf8mb4;
```

### Alternative: Individual Options

Instead of a connection string, you can specify parameters individually:

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

If `ConnectionString` is provided, it takes precedence over individual options.

### Connection Option Reference

| Option | Default | Description |
|---|---|---|
| `server` | `localhost` | MySQL server hostname or IP address |
| `port` | `3306` | MySQL server port |
| `database` | `jellyfin` | Database name |
| `user` | `jellyfin` | MySQL username |
| `password` | *(empty)* | MySQL password |
| `sslmode` | `Preferred` | SSL mode: `None`, `Preferred`, or `Required` |
| `EnableSensitiveDataLogging` | `False` | Log parameter values in EF Core queries (debug only) |

### Locking Behavior

The `LockingBehavior` field controls EF Core concurrency handling:

| Value | Description |
|---|---|
| `NoLock` | No additional concurrency control (recommended for MySQL) |
| `Pessimistic` | Database-level row locking |
| `Optimistic` | Row version-based conflict detection |

---

## Run Jellyfin from the Command Line

### Basic Usage

Run the server directly with `dotnet run`:

```bash
cd /path/to/jellyfin
dotnet run --project Jellyfin.Server
```

Or run the compiled binary:

```bash
dotnet Jellyfin.Server/bin/Debug/net10.0/jellyfin.dll
```

### Command-Line Options

| Flag | Description |
|---|---|
| `-d`, `--datadir <path>` | Data folder path (database backups, trickplay, etc.) |
| `-c`, `--configdir <path>` | Configuration directory (where `database.json` lives) |
| `-C`, `--cachedir <path>` | Cache directory |
| `--ffmpeg <path>` | Path to FFmpeg executable |

### Example: Custom Directories

```bash
dotnet run --project Jellyfin.Server -- \
  --datadir /srv/jellyfin/data \
  --configdir /etc/jellyfin \
  --cachedir /var/cache/jellyfin
```

With this layout, place `database.json` at `/etc/jellyfin/database.json`.

### Environment Variables

Paths can also be set via environment variables instead of CLI flags:

```bash
export JELLYFIN_DATA_DIR=/srv/jellyfin/data
export JELLYFIN_CONFIG_DIR=/etc/jellyfin
export JELLYFIN_CACHE_DIR=/var/cache/jellyfin
export JELLYFIN_LOG_DIR=/var/log/jellyfin

dotnet run --project Jellyfin.Server
```

### Verify the Connection

On first start with a MySQL configuration, Jellyfin runs EF Core migrations to create the schema. Watch the console output for:

```
MySQL connection string: Server=localhost;Port=3306;Database=jellyfin;...
MySQL connection interceptor command set to: SET SESSION wait_timeout=28800; ...
```

Once running, access the web UI at `http://localhost:8096` to complete setup.

### Verify Tables Were Created

```bash
sudo mysql -u jellyfin -p jellyfin -e "SHOW TABLES;"
```

You should see tables including `BaseItems`, `Users`, `UserData`, `MediaStreamInfos`, `ActivityLogs`, and others.

---

## Switching from SQLite to MySQL

If you have an existing Jellyfin installation using SQLite and want to switch to MySQL:

1. Stop Jellyfin.
2. Create the MySQL database and user as described above.
3. Replace the contents of `database.json` with the MySQL configuration.
4. Start Jellyfin. The MySQL schema will be created via EF Core migrations.

Data from the existing SQLite database is **not** automatically migrated. You will start with a fresh database. Back up your SQLite database (`jellyfin.db`) before switching.

---

## Backup and Restore

### Backups

Migration safety backups are stored automatically at:

```
{DataPath}/MySqlBackups/{timestamp}/
```

Each backup contains one JSON file per table (e.g., `Users.json`, `BaseItems.json`).

### Manual Backup with mysqldump

For full manual backups:

```bash
mysqldump -u jellyfin -p jellyfin > jellyfin_backup_$(date +%Y%m%d).sql
```

### Restore from mysqldump

```bash
mysql -u jellyfin -p jellyfin < jellyfin_backup_20260204.sql
```

---

## Troubleshooting

### Connection Refused

Verify MySQL is running and accepting connections:

```bash
sudo systemctl status mysql
mysql -u jellyfin -p -e "SELECT 1;"
```

### Access Denied

Verify the user and privileges:

```bash
sudo mysql -e "SELECT user, host FROM mysql.user WHERE user='jellyfin';"
sudo mysql -e "SHOW GRANTS FOR 'jellyfin'@'localhost';"
```

### Character Set Issues

The provider sets `NAMES utf8mb4` on every connection. Verify the database was created with the correct character set:

```bash
sudo mysql -e "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='jellyfin';"
```

Expected output: `utf8mb4` / `utf8mb4_unicode_ci`.

### Checking Logs

Jellyfin logs to stdout by default when run from the command line. Logs are also written to:

```
{DataPath}/log/
```

Look for lines containing `MySQL` to diagnose connection or migration issues.
