# Migrating Jellyfin MySQL to Another Server

This guide covers exporting a Jellyfin MySQL database from one server, transferring it to a new MySQL server, moving the required configuration files, installing the necessary .NET libraries and packages, and starting Jellyfin on the new host.

---

## 1. Install .NET SDK and Required Packages

### .NET 10 SDK

Jellyfin targets `net10.0`. Install the .NET 10 SDK on the new server:

```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0
```

Add to your shell profile (`~/.bashrc` or `~/.profile`) for persistence:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
```

Verify:

```bash
dotnet --version
# Should output 10.0.x
```

### EF Core Tools (optional, for manual migration management)

```bash
dotnet tool install --global dotnet-ef
```

### NuGet Package Dependencies

The MySQL provider requires the following NuGet packages, which are restored automatically when building from source:

| Package | Version | Purpose |
|---|---|---|
| `Pomelo.EntityFrameworkCore.MySql` | `10.0.0-preview.1.efcore.10.0.0` | MySQL/MariaDB EF Core provider |
| `Microsoft.EntityFrameworkCore.Relational` | `10.0.2` | EF Core relational database abstractions |
| `Microsoft.EntityFrameworkCore.Design` | `10.0.2` | EF Core design-time tools (migrations) |
| `Microsoft.EntityFrameworkCore.Tools` | `10.0.2` | EF Core CLI tooling |

### Pomelo NuGet Package Setup

The Pomelo MySQL provider for EF Core 10 is currently a preview release. It must be available as a local NuGet source. Download the package and configure the source:

```bash
# Create the local package directory
sudo mkdir -p /tmp/pomelo-nupkg

# Download the Pomelo preview package
# (copy from your source server or download from the Pomelo GitHub releases)
scp user@source-server:/tmp/pomelo-nupkg/*.nupkg /tmp/pomelo-nupkg/
```

Ensure the project's `nuget.config` includes the local source. This file should already be present in the repository root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
    <add key="local-pomelo" value="/tmp/pomelo-nupkg" />
  </packageSources>
</configuration>
```

### Build Dependencies (Ubuntu/Debian)

```bash
sudo apt update
sudo apt install -y git build-essential libfontconfig1-dev ffmpeg
```

### Build the Solution

Clone (or copy) the repository on the new server and restore packages:

```bash
git clone https://github.com/jellyfin/jellyfin.git
cd jellyfin
dotnet restore Jellyfin.sln
dotnet build Jellyfin.sln
```

For a release build:

```bash
dotnet build Jellyfin.sln -c Release
```

The MySQL provider project (`Jellyfin.Database.Providers.MySql`) is compiled as part of the solution via its project reference in `Jellyfin.Server.Implementations.csproj`. No separate build step is needed.

---

## 2. Stop Jellyfin on the Source Server

Before exporting, stop the running Jellyfin instance to ensure a consistent database snapshot:

```bash
# If running as a systemd service
sudo systemctl stop jellyfin

# If running manually, press Ctrl+C or kill the process
```

---

## 3. Export the MySQL Database

Use `mysqldump` to create a full export of the Jellyfin database:

```bash
mysqldump -u jellyfin -p --single-transaction --routines --triggers jellyfin > jellyfin_backup.sql
```

- `--single-transaction` ensures a consistent snapshot without locking tables (InnoDB).
- `--routines` includes stored procedures/functions if any exist.
- `--triggers` includes triggers if any exist.

Verify the dump file was created and is not empty:

```bash
ls -lh jellyfin_backup.sql
```

---

## 4. Identify and Copy Configuration Files

Jellyfin stores its configuration in a config directory. The location depends on how Jellyfin was started:

| Method | Default Config Path |
|---|---|
| `--configdir` CLI flag | Value of the flag |
| `$JELLYFIN_CONFIG_DIR` env var | Value of the variable |
| `--datadir` / `$JELLYFIN_DATA_DIR` | `{datadir}/config` |
| Default (Linux) | `~/.local/share/jellyfin/config` |
| Default (package install) | `/etc/jellyfin` |

The key files to copy are:

| File | Purpose |
|---|---|
| `database.json` | Database connection configuration (required) |
| `database.xml` | Alternative XML database configuration |
| `system.xml` | Server settings (ports, paths, library config) |
| `network.xml` | Network/binding settings |
| `encoding.xml` | Transcoding/FFmpeg settings |
| `logging.default.json` | Logging configuration |

### Archive the config directory

```bash
# Replace with your actual config path
CONFIG_DIR="~/.local/share/jellyfin/config"

tar czf jellyfin_config.tar.gz -C "$(dirname $CONFIG_DIR)" "$(basename $CONFIG_DIR)"
```

### Copy the data directory (optional but recommended)

The data directory contains metadata, images, trickplay files, and internal backups. If you want to preserve these on the new server:

```bash
# Replace with your actual data path
DATA_DIR="~/.local/share/jellyfin"

tar czf jellyfin_data.tar.gz -C "$(dirname $DATA_DIR)" "$(basename $DATA_DIR)" \
    --exclude="*/cache" \
    --exclude="*/log"
```

---

## 5. Transfer Files to the New Server

Copy the dump and config archive to the new server:

```bash
scp jellyfin_backup.sql user@new-server:/tmp/
scp jellyfin_config.tar.gz user@new-server:/tmp/

# If transferring the data directory as well
scp jellyfin_data.tar.gz user@new-server:/tmp/
```

---

## 6. Set Up MySQL on the New Server

### Create the database and user

Connect to MySQL on the new server:

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

If Jellyfin will connect from a remote host, replace `'localhost'` with the appropriate host or `'%'`:

```sql
CREATE USER 'jellyfin'@'%' IDENTIFIED BY 'your_secure_password';
GRANT ALL PRIVILEGES ON jellyfin.* TO 'jellyfin'@'%';
FLUSH PRIVILEGES;
```

### Import the database dump

```bash
mysql -u jellyfin -p jellyfin < /tmp/jellyfin_backup.sql
```

Verify the tables were imported:

```bash
mysql -u jellyfin -p jellyfin -e "SHOW TABLES;"
```

You should see tables including `BaseItems`, `Users`, `UserData`, `MediaStreamInfos`, `ActivityLogs`, and others.

---

## 7. Restore Configuration Files

Extract the config archive to the desired location on the new server:

```bash
# Example: restore to the default location
mkdir -p ~/.local/share/jellyfin
tar xzf /tmp/jellyfin_config.tar.gz -C ~/.local/share/jellyfin/

# If restoring the data directory
tar xzf /tmp/jellyfin_data.tar.gz -C ~/.local/share/
```

Or for a package-based install:

```bash
sudo tar xzf /tmp/jellyfin_config.tar.gz -C /etc/
```

### Update database.json for the new server

Edit `database.json` in the config directory to reflect the new MySQL server's connection details:

```json
{
  "DatabaseType": "Jellyfin-MySQL",
  "LockingBehavior": "NoLock",
  "CustomProviderOptions": {
    "ConnectionString": "Server=localhost;Port=3306;Database=jellyfin;User=jellyfin;Password=your_secure_password;SslMode=Preferred;CharSet=utf8mb4;"
  }
}
```

Update the `Server`, `Password`, and any other values that differ on the new host.

### Update system.xml paths (if directory layout changed)

If media library paths, FFmpeg path, or other directory paths differ on the new server, edit `system.xml` to update them.

---

## 8. Start Jellyfin on the New Server

### Using dotnet run (from source)

```bash
cd /path/to/jellyfin
dotnet run --project Jellyfin.Server
```

### Using the compiled binary

```bash
dotnet /path/to/jellyfin/Jellyfin.Server/bin/Debug/net10.0/jellyfin.dll
```

### With custom directories

```bash
dotnet run --project Jellyfin.Server -- \
  --datadir /srv/jellyfin/data \
  --configdir /etc/jellyfin \
  --cachedir /var/cache/jellyfin
```

### Using environment variables

```bash
export JELLYFIN_DATA_DIR=/srv/jellyfin/data
export JELLYFIN_CONFIG_DIR=/etc/jellyfin
export JELLYFIN_CACHE_DIR=/var/cache/jellyfin
export JELLYFIN_LOG_DIR=/var/log/jellyfin

dotnet run --project Jellyfin.Server
```

### As a systemd service

If Jellyfin is installed as a package:

```bash
sudo systemctl start jellyfin
```

---

## 9. Verify the Migration

1. Watch the startup logs for a successful MySQL connection:

   ```
   MySQL connection string: Server=localhost;Port=3306;Database=jellyfin;...
   MySQL connection interceptor command set to: SET SESSION wait_timeout=28800; ...
   ```

2. Open the web UI at `http://new-server:8096` and confirm:
   - You can log in with your existing user accounts
   - Libraries and media metadata are intact
   - Playback and transcoding work correctly

3. Check the database directly:

   ```bash
   mysql -u jellyfin -p jellyfin -e "SELECT COUNT(*) FROM BaseItems;"
   ```

---

## Troubleshooting

### Connection Refused

Verify MySQL is running and listening:

```bash
sudo systemctl status mysql
mysql -u jellyfin -p -e "SELECT 1;"
```

If MySQL is on a remote host, ensure the `bind-address` in `/etc/mysql/mysql.conf.d/mysqld.cnf` allows remote connections and that firewall rules permit port 3306.

### Access Denied

Verify the user exists and has the correct grants:

```bash
sudo mysql -e "SELECT user, host FROM mysql.user WHERE user='jellyfin';"
sudo mysql -e "SHOW GRANTS FOR 'jellyfin'@'localhost';"
```

### Character Set Mismatch

The MySQL provider sets `NAMES utf8mb4` on every connection. Confirm the database uses the correct character set:

```bash
mysql -u jellyfin -p -e "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='jellyfin';"
```

Expected: `utf8mb4` / `utf8mb4_unicode_ci`.

### EF Core Migration Errors

If the schema version on the new server doesn't match, Jellyfin will attempt to run pending EF Core migrations on startup. If migrations fail, check that the dump was created from the same Jellyfin version that is running on the new server.
