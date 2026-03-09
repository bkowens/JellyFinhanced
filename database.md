# Jellyfin Data Storage Architecture

This document describes where and how Jellyfin stores its data, covering the primary database, file-based configuration, caching, and media metadata systems.

---

## Table of Contents

- [Overview](#overview)
- [Primary Database (SQLite)](#primary-database-sqlite)
  - [Connection and Configuration](#connection-and-configuration)
  - [Database Provider Architecture](#database-provider-architecture)
  - [Concurrency and Locking](#concurrency-and-locking)
  - [Scheduled Optimization](#scheduled-optimization)
- [MySQL / MariaDB Provider](#mysql--mariadb-provider)
  - [MySQL Setup](#mysql-setup)
  - [MySQL Configuration](#mysql-configuration)
  - [MySQL Connection Options](#mysql-connection-options)
  - [MySQL Optimization](#mysql-optimization)
  - [MySQL Backup and Restore](#mysql-backup-and-restore)
- [Database Schema](#database-schema)
  - [Users and Authentication](#users-and-authentication)
  - [Media Items](#media-items)
  - [User-Item Relationships](#user-item-relationships)
  - [Display Preferences](#display-preferences)
  - [People and Credits](#people-and-credits)
  - [Media Streams and Attachments](#media-streams-and-attachments)
  - [Chapters and Segments](#chapters-and-segments)
  - [Images](#images)
  - [Trickplay Metadata](#trickplay-metadata)
  - [Keyframe Data](#keyframe-data)
  - [Item Values and Filtering](#item-values-and-filtering)
  - [Activity Logging](#activity-logging)
  - [Devices and API Keys](#devices-and-api-keys)
- [Entity Relationships](#entity-relationships)
- [File-Based Storage](#file-based-storage)
  - [Directory Layout](#directory-layout)
  - [Configuration Files (XML/JSON)](#configuration-files-xmljson)
  - [Media Library Definitions](#media-library-definitions)
  - [Metadata on Disk](#metadata-on-disk)
  - [Image Cache](#image-cache)
  - [Trickplay Files](#trickplay-files)
  - [Transcoding Temporary Files](#transcoding-temporary-files)
  - [Plugin Storage](#plugin-storage)
- [Migrations](#migrations)
  - [Legacy Databases](#legacy-databases)
  - [Migration Routines](#migration-routines)
- [Backup and Restore](#backup-and-restore)
- [Data Access Patterns](#data-access-patterns)

---

## Overview

Jellyfin uses a **single database** as its primary data store, accessed via **Entity Framework Core**. The default provider is **SQLite** (`jellyfin.db`), with **MySQL/MariaDB** available as a built-in alternative. Supplementary data is stored in **XML configuration files**, **on-disk metadata directories**, and **cached image files**. The architecture supports pluggable database providers via the `IJellyfinDatabaseProvider` interface.

Key source files:

| Component | File |
|---|---|
| DbContext | `src/Jellyfin.Database/Jellyfin.Database.Implementations/JellyfinDbContext.cs` |
| SQLite Provider | `src/Jellyfin.Database/Jellyfin.Database.Providers.Sqlite/SqliteDatabaseProvider.cs` |
| MySQL Provider | `src/Jellyfin.Database/Jellyfin.Database.Providers.MySql/MySqlDatabaseProvider.cs` |
| DI Registration | `Jellyfin.Server.Implementations/Extensions/ServiceCollectionExtensions.cs` |
| Application Paths | `Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs` |
| Server Paths | `Emby.Server.Implementations/ServerApplicationPaths.cs` |

---

## Primary Database (SQLite)

### Connection and Configuration

The database file is located at:

```
{DataPath}/jellyfin.db
```

Where `{DataPath}` defaults to `{ProgramDataPath}/data`. The `ProgramDataPath` varies by platform:

| Platform | Default ProgramDataPath |
|---|---|
| Linux | `~/.local/share/jellyfin/` or `$JELLYFIN_DATA_DIR` |
| macOS | `~/.local/share/jellyfin/` |
| Windows | `%APPDATA%/jellyfin/` |

Database behavior is configured via `{ConfigurationDirectoryPath}/database.json`:

```json
{
  "DatabaseType": "Jellyfin-SQLite",
  "LockingBehavior": "NoLock"
}
```

SQLite connection parameters (set in `SqliteDatabaseProvider.Initialise()`):

| Parameter | Default | Description |
|---|---|---|
| Cache Mode | `Default` | SQLite cache sharing mode |
| Pooling | `true` | Connection pooling |
| Command Timeout | `30` seconds | Query timeout |
| Journal Size Limit | `128 MB` | WAL journal max size |
| Sync Mode | `1` (NORMAL) | fsync behavior |
| Temp Store Mode | `2` (MEMORY) | Temp tables in memory |
| Locking Mode | `NORMAL` | SQLite locking mode |

Custom SQLite PRAGMAs can be injected via the `#PRAGMA:` prefix in custom provider options.

### Database Provider Architecture

Jellyfin abstracts database access behind the `IJellyfinDatabaseProvider` interface (`src/Jellyfin.Database/Jellyfin.Database.Implementations/IJellyfinDatabaseProvider.cs`), allowing alternative database engines to be loaded as plugins.

Built-in providers:

| Provider Key | Class | Package |
|---|---|---|
| `Jellyfin-SQLite` | `SqliteDatabaseProvider` | `Microsoft.EntityFrameworkCore.Sqlite` |
| `Jellyfin-MySQL` | `MySqlDatabaseProvider` | `MySql.EntityFrameworkCore` |

Provider resolution order (`ServiceCollectionExtensions.AddJellyfinDbContext()`):

1. Read `database.json` configuration
2. Check for `--migration-provider` command-line argument
3. Fall back to `Jellyfin-SQLite` with `NoLock` behavior
4. If `DatabaseType` is `PLUGIN_PROVIDER`, load from plugins directory

The context is registered as a **pooled DbContextFactory** (`IDbContextFactory<JellyfinDbContext>`) for thread-safe access.

### Concurrency and Locking

Three locking strategies are available (configured via `LockingBehavior` in `database.json`):

| Mode | Class | Behavior |
|---|---|---|
| `NoLock` | `NoLockBehavior` | No additional concurrency control (default) |
| `Pessimistic` | `PessimisticLockBehavior` | Database-level row locking |
| `Optimistic` | `OptimisticLockBehavior` | Row version-based conflict detection |

Entities implementing `IHasConcurrencyToken` have a `RowVersion` property that auto-increments on save, enabling optimistic concurrency detection. Used by: `User`, `Permission`, `Preference`, `ActivityLog`.

### Scheduled Optimization

On shutdown (`RunShutdownTask`), the provider runs:

```sql
PRAGMA optimize
```

The scheduled optimization task (`RunScheduledOptimisation`) runs:

```sql
PRAGMA wal_checkpoint(TRUNCATE)
PRAGMA optimize
VACUUM
PRAGMA wal_checkpoint(TRUNCATE)
```

---

## MySQL / MariaDB Provider

Jellyfin includes a built-in MySQL/MariaDB provider using `MySql.EntityFrameworkCore` (Oracle's official MySQL EF Core provider). This allows Jellyfin to use a MySQL or MariaDB server as its primary database instead of SQLite.

### MySQL Setup

1. Create a MySQL database and user:

```sql
CREATE DATABASE jellyfin CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'jellyfin'@'localhost' IDENTIFIED BY 'your_password';
GRANT ALL PRIVILEGES ON jellyfin.* TO 'jellyfin'@'localhost';
FLUSH PRIVILEGES;
```

2. Create or edit `{ConfigurationDirectoryPath}/database.json`:

```json
{
  "DatabaseType": "Jellyfin-MySQL",
  "LockingBehavior": "NoLock",
  "CustomProviderOptions": {
    "ConnectionString": "Server=localhost;Port=3306;Database=jellyfin;User=jellyfin;Password=your_password;SslMode=Preferred;CharSet=utf8mb4;"
  }
}
```

3. Restart Jellyfin. The MySQL provider will run EF Core migrations automatically to create the schema.

### MySQL Configuration

Database behavior is configured via `{ConfigurationDirectoryPath}/database.json`:

```json
{
  "DatabaseType": "Jellyfin-MySQL",
  "LockingBehavior": "NoLock",
  "CustomProviderOptions": {
    "ConnectionString": "Server=localhost;Port=3306;Database=jellyfin;User=jellyfin;Password=your_password;"
  }
}
```

Alternatively, connection parameters can be specified individually via custom options:

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
      { "Key": "password", "Value": "your_password" },
      { "Key": "sslmode", "Value": "Preferred" }
    ]
  }
}
```

### MySQL Connection Options

| Parameter | Default | Description |
|---|---|---|
| `server` | `localhost` | MySQL server hostname or IP |
| `port` | `3306` | MySQL server port |
| `database` | `jellyfin` | Database name |
| `user` | `jellyfin` | MySQL username |
| `password` | (empty) | MySQL password |
| `sslmode` | `Preferred` | SSL mode (`None`, `Preferred`, `Required`) |

Session-level settings applied on each connection open (via `MySqlConnectionInterceptor`):

| Setting | Value | Description |
|---|---|---|
| `wait_timeout` | `28800` | Connection idle timeout (8 hours) |
| `interactive_timeout` | `28800` | Interactive connection timeout |
| `NAMES` | `utf8mb4` | Character set for connection |

### MySQL Optimization

The scheduled optimization task (`RunScheduledOptimisation`) queries `information_schema.tables` for all user tables and runs:

```sql
OPTIMIZE TABLE `table_name`
```

on each table. This reclaims unused space and defragments the data file for InnoDB tables.

No shutdown task is needed for MySQL as connections are managed by the server.

### MySQL Backup and Restore

Migration safety backups are stored at `{DataPath}/MySqlBackups/{timestamp}/`. Each backup contains one JSON file per table with all row data exported.

Backup location: `{DataPath}/MySqlBackups/{timestamp}/{table_name}.json`

---

## Database Schema

All entities are defined in `src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/` with EF Core configurations in the `ModelConfiguration/` subdirectory. The `JellyfinDbContext` class exposes these as `DbSet<T>` properties.

### Users and Authentication

**`Users`** - User accounts

| Column | Type | Description |
|---|---|---|
| `Id` | Guid (PK) | Unique user identifier |
| `Username` | string (255) | Display name |
| `Password` | string (65535) | Hashed password |
| `AuthenticationProviderId` | string | Auth provider class name |
| `PasswordResetProviderId` | string | Password reset provider class name |
| `InvalidLoginAttemptCount` | int | Failed login counter |
| `LastLoginDate` | DateTime? | Last successful login |
| `LastActivityDate` | DateTime? | Last API activity |
| `SubtitleMode` | enum | Subtitle display preference |
| `AudioLanguagePreference` | string? | Preferred audio language |
| `SubtitleLanguagePreference` | string? | Preferred subtitle language |
| `MaxParentalAgeRating` | int? | Parental control limit |
| `RemoteClientBitrateLimit` | int? | Bandwidth cap for remote playback |
| `MaxActiveSessions` | int | Session limit (0 = unlimited) |
| `SyncPlayAccess` | enum | SyncPlay permission level |
| `RowVersion` | uint | Concurrency token |

**Related tables:**

- **`Permissions`** - Boolean permission flags per user (`PermissionKind` enum + `Value`)
- **`Preferences`** - Key-value user preferences (`PreferenceKind` enum + `Value` string up to 65535 chars)
- **`AccessSchedules`** - Time-based access restrictions (`DayOfWeek`, `StartHour`, `EndHour`)

### Media Items

**`BaseItems`** - All media library items (movies, series, episodes, albums, songs, folders, etc.)

| Column | Type | Description |
|---|---|---|
| `Id` | Guid (PK) | Item identifier |
| `Type` | string | .NET type name (e.g., `"Movie"`, `"Series"`, `"Episode"`) |
| `Path` | string? | File system path to media file |
| `Name` | string? | Display name |
| `SortName` | string? | Name used for sorting |
| `ForcedSortName` | string? | User-override sort name |
| `Overview` | string? | Plot summary / description |
| `Tagline` | string? | Short tagline |
| `Genres` | string? | Pipe-delimited genre list |
| `Studios` | string? | Studio names |
| `Artists` | string? | Artist names (music) |
| `AlbumArtists` | string? | Album artist names (music) |
| `Album` | string? | Album name (music) |
| `CommunityRating` | float? | Average user rating |
| `OfficialRating` | string? | Content rating (PG-13, TV-MA, etc.) |
| `ProductionYear` | int? | Release year |
| `PremiereDate` | DateTime? | Original air/release date |
| `RunTimeTicks` | long? | Duration in ticks (100ns units) |
| `IsFolder` | bool | Whether item is a container |
| `IsVirtualItem` | bool | Virtual (unaired/missing) item |
| `IsLocked` | bool | Prevent metadata updates |
| `ChannelId` | Guid? | Associated channel |
| `ParentId` | Guid? | Parent item in hierarchy |
| `SeasonId` | Guid? | Season reference (episodes) |
| `SeriesId` | Guid? | Series reference (episodes/seasons) |
| `TopParentId` | Guid? | Root library item |
| `MediaType` | string? | `"Video"`, `"Audio"`, `"Photo"`, etc. |
| `Width` | int? | Video/image width |
| `Height` | int? | Video/image height |
| `DateCreated` | DateTime? | When item was added |
| `DateModified` | DateTime? | Last file modification |
| `DateLastRefreshed` | DateTime? | Last metadata refresh |
| `DateLastSaved` | DateTime? | Last database save |
| `ExtraType` | int? | Extra content type (trailer, featurette, etc.) |
| `ExternalId` | string? | External provider IDs (serialized) |
| `Data` | string? | Serialized additional data |

**`BaseItemProviders`** - External provider ID mappings (TMDB, IMDB, TVDB, etc.)

| Column | Type | Description |
|---|---|---|
| `ItemId` | Guid (FK) | Reference to BaseItems |
| `ProviderId` | string | Provider identifier value |
| `ProviderName` | string | Provider name key |

**`BaseItemMetadataFields`** - Tracks which metadata fields are locked from automatic updates.

**`BaseItemTrailerTypes`** - Categorizes trailer items (clip, behind-the-scenes, etc.).

**`AncestorIds`** - Flattened parent-child hierarchy for efficient querying of items within library trees.

| Column | Type | Description |
|---|---|---|
| `ItemId` | Guid (FK) | Child item |
| `ParentItemId` | Guid (FK) | Ancestor item |

### User-Item Relationships

**`UserData`** - Per-user state for each media item

| Column | Type | Description |
|---|---|---|
| `UserId` | Guid (PK, FK) | User reference |
| `ItemId` | Guid (PK, FK) | Item reference |
| `PlaybackPositionTicks` | long | Resume position |
| `PlayCount` | int | Times played |
| `IsFavorite` | bool | Favorited |
| `Played` | bool | Marked as watched |
| `Rating` | double? | User rating (0-10) |
| `LastPlayedDate` | DateTime? | Last playback time |
| `AudioStreamIndex` | int? | Preferred audio track |
| `SubtitleStreamIndex` | int? | Preferred subtitle track |
| `Likes` | bool? | Thumbs up/down |
| `RetentionDate` | DateTime? | For retention tracking of deleted items |

### Display Preferences

**`DisplayPreferences`** - View settings per user, item, and client application

| Column | Type | Description |
|---|---|---|
| `Id` | int (PK) | Auto-increment ID |
| `UserId` | Guid (FK) | User reference |
| `ItemId` | Guid | Context item |
| `Client` | string (32) | Client application name |
| `ShowSidebar` | bool | Sidebar visibility |
| `ShowBackdrop` | bool | Backdrop display |
| `SkipForwardLength` | int | Skip forward seconds |
| `SkipBackwardLength` | int | Skip backward seconds |
| `ScrollDirection` | enum | Scroll orientation |
| `ChromecastVersion` | enum | Chromecast receiver version |
| `HomeSections` | string? | Home screen layout |

**`ItemDisplayPreferences`** - Per-item view type, sort, and remembering positions.

**`CustomItemDisplayPreferences`** - Arbitrary key-value display settings per user/item/client.

### People and Credits

**`Peoples`** - Actors, directors, writers, and other people

| Column | Type | Description |
|---|---|---|
| `Id` | Guid (PK) | Person identifier |
| `Name` | string | Full name |
| `PersonType` | string? | Category (Actor, Director, Writer, etc.) |

**`PeopleBaseItemMap`** - Junction table linking people to items with roles

| Column | Type | Description |
|---|---|---|
| `Id` | Guid (PK) | Map entry ID |
| `ItemId` | Guid (FK) | Media item reference |
| `PeopleId` | Guid (FK) | Person reference |
| `Role` | string? | Character or role name |
| `PersonType` | string? | Contribution type |
| `SortOrder` | int? | Display ordering |
| `ListOrder` | int? | List ordering |

### Media Streams and Attachments

**`MediaStreamInfos`** - Video, audio, and subtitle tracks within a media file

| Column | Type | Description |
|---|---|---|
| `ItemId` | Guid (FK) | Parent media item |
| `StreamIndex` | int | Track index |
| `StreamType` | enum | Video / Audio / Subtitle / EmbeddedImage |
| `Codec` | string? | Codec name (h264, aac, srt, etc.) |
| `Language` | string? | ISO language code |
| `ChannelLayout` | string? | Audio channel layout (5.1, 7.1, etc.) |
| `Profile` | string? | Codec profile |
| `AspectRatio` | string? | Display aspect ratio |
| `Width` | int? | Video width |
| `Height` | int? | Video height |
| `BitRate` | int? | Stream bitrate |
| `SampleRate` | int? | Audio sample rate |
| `Channels` | int? | Audio channel count |
| `IsDefault` | bool | Default track flag |
| `IsForced` | bool | Forced subtitle flag |
| `IsExternal` | bool | External file (not embedded) |
| `IsInterlaced` | bool | Interlaced video flag |
| `IsHearingImpaired` | bool | SDH/CC flag |
| `BitDepth` | int? | Color bit depth |
| `DvVersionMajor` | int? | Dolby Vision version |
| `DvProfile` | int? | Dolby Vision profile |
| `DvLevel` | int? | Dolby Vision level |
| `IsHdr10Plus` | bool | HDR10+ flag |
| `Rotation` | int? | Video rotation degrees |
| `KeyFrames` | string? | Keyframe positions (serialized) |

**`AttachmentStreamInfos`** - Non-media attachments embedded in files (fonts, thumbnails, etc.)

### Chapters and Segments

**`Chapters`** - Chapter/scene markers

| Column | Type | Description |
|---|---|---|
| `ItemId` | Guid (FK) | Parent media item |
| `ChapterIndex` | int | Ordered chapter number |
| `StartPositionTicks` | long | Chapter start time |
| `Name` | string? | Chapter title |
| `ImagePath` | string? | Chapter thumbnail path |
| `ImageDateModified` | DateTime? | Thumbnail modification date |

**`MediaSegments`** - Detected or manually defined content segments

| Column | Type | Description |
|---|---|---|
| `Id` | Guid (PK) | Segment identifier |
| `ItemId` | Guid (FK) | Parent media item |
| `Type` | enum | Intro, Outro, Preroll, Recap, Preview, Commercial, etc. |
| `StartTicks` | long | Segment start time |
| `EndTicks` | long | Segment end time |
| `SegmentProviderId` | string | Provider that detected this segment |

### Images

**`BaseItemImageInfos`** - Image metadata for media items

| Column | Type | Description |
|---|---|---|
| `Id` | Guid (PK) | Image record ID |
| `ItemId` | Guid (FK) | Parent media item |
| `Path` | string | File path to image |
| `ImageType` | enum | Primary, Backdrop, Banner, Thumb, Logo, Art, etc. |
| `Width` | int | Image width |
| `Height` | int | Image height |
| `Blurhash` | byte[]? | BlurHash placeholder data |
| `DateModified` | DateTime | Last modification time |

**`ImageInfos`** - General image info (used outside base items).

### Trickplay Metadata

**`TrickplayInfos`** - Metadata for trickplay (seek preview) tile grids. Actual image tiles are stored on disk (see [Trickplay Files](#trickplay-files)).

| Column | Type | Description |
|---|---|---|
| `ItemId` | Guid (FK) | Media item reference |
| `Width` | int | Individual thumbnail width |
| `Height` | int | Individual thumbnail height |
| `TileWidth` | int | Thumbnails per row in grid |
| `TileHeight` | int | Thumbnails per column in grid |
| `ThumbnailCount` | int | Total thumbnails generated |
| `Interval` | int | Milliseconds between thumbnails |
| `Bandwidth` | int | Peak bandwidth (bits/sec) |

### Keyframe Data

**`KeyframeData`** - Video keyframe positions for efficient seeking and segment-accurate splitting.

| Column | Type | Description |
|---|---|---|
| `ItemId` | Guid (FK) | Media item reference |
| `TotalDuration` | long | Full duration in ticks |
| `KeyframeTicks` | string | Serialized list of keyframe positions |

### Item Values and Filtering

**`ItemValues`** - Normalized, deduplicated values used for search and filtering.

| Column | Type | Description |
|---|---|---|
| `ItemValueId` | Guid (PK) | Value identifier |
| `Type` | enum | Artist, AlbumArtist, Studios, Genre |
| `Value` | string | Display value |
| `CleanValue` | string | Sanitized value for matching |

**`ItemValuesMap`** - Many-to-many junction table linking `BaseItems` to `ItemValues`.

### Activity Logging

**`ActivityLogs`** - Server event log

| Column | Type | Description |
|---|---|---|
| `Id` | int (PK) | Auto-increment ID |
| `Name` | string (512) | Event name |
| `Type` | string (512) | Event type category |
| `Overview` | string? (512) | Detailed description |
| `ShortOverview` | string? (512) | Brief description |
| `UserId` | Guid | Associated user |
| `ItemId` | string? | Associated item reference |
| `DateCreated` | DateTime | Event timestamp (UTC) |
| `LogSeverity` | LogLevel | Severity (Info, Warning, Error, etc.) |
| `RowVersion` | uint | Concurrency token |

### Devices and API Keys

**`Devices`** - Connected client devices and sessions

| Column | Type | Description |
|---|---|---|
| `Id` | int (PK) | Auto-increment ID |
| `UserId` | Guid (FK) | Owner user |
| `DeviceId` | string (64) | Client-reported device ID |
| `DeviceName` | string (64) | Client-reported device name |
| `AppName` | string (64) | Client application name |
| `AppVersion` | string (32) | Client version |
| `AccessToken` | Guid | Authentication token |
| `IsActive` | bool | Session active flag |
| `DateCreated` | DateTime | First connection |
| `DateModified` | DateTime | Last token refresh |
| `DateLastActivity` | DateTime | Last API call |

**`DeviceOptions`** - Per-device administrative settings (custom name, etc.).

**`ApiKeys`** - Manually created API tokens (not tied to user sessions)

| Column | Type | Description |
|---|---|---|
| `Id` | int (PK) | Auto-increment ID |
| `Name` | string (64) | Key label |
| `AccessToken` | Guid | Token value |
| `DateCreated` | DateTime | Creation time |
| `DateLastActivity` | DateTime | Last use |

---

## Entity Relationships

```
User (1) ──── (*) Permission
  │
  ├──── (*) Preference
  │
  ├──── (*) AccessSchedule
  │
  ├──── (*) DisplayPreferences ──── (*) CustomItemDisplayPreferences
  │
  ├──── (*) ItemDisplayPreferences
  │
  ├──── (*) UserData ────── (1) BaseItemEntity
  │
  └──── (*) Device

BaseItemEntity (1) ──── (*) BaseItemImageInfo
  │
  ├──── (*) MediaStreamInfo
  │
  ├──── (*) AttachmentStreamInfo
  │
  ├──── (*) Chapter
  │
  ├──── (*) MediaSegment
  │
  ├──── (*) BaseItemProvider
  │
  ├──── (*) BaseItemMetadataField
  │
  ├──── (*) BaseItemTrailerType
  │
  ├──── (*) AncestorId (parent/child hierarchy)
  │
  ├──── (1) TrickplayInfo
  │
  ├──── (1) KeyframeData
  │
  ├──── (*) PeopleBaseItemMap ────── (1) People
  │
  └──── (*) ItemValueMap ────── (1) ItemValue
```

---

## File-Based Storage

### Directory Layout

```
{ProgramDataPath}/
├── data/
│   ├── jellyfin.db                  # Primary SQLite database
│   ├── SQLiteBackups/               # Migration safety backups
│   │   └── {timestamp}_jellyfin.db
│   ├── trickplay/                   # Seek preview thumbnail grids
│   ├── collections/                 # Collection definitions
│   ├── playlists/                   # Playlist data
│   ├── subtitles/                   # Downloaded subtitle cache
│   ├── backups/                     # Full system backup ZIPs
│   └── ScheduledTasks/              # Scheduled task state
├── metadata/                        # Internal metadata and images
│   ├── People/                      # Person images
│   ├── artists/                     # Artist images
│   ├── Genre/                       # Genre images
│   ├── MusicGenre/                  # Music genre images
│   ├── Studio/                      # Studio images
│   └── Year/                        # Year images
├── root/
│   └── default/                     # Virtual folder (library) definitions
│       └── {library_name}/
│           ├── options.xml          # Library settings and media paths
│           └── {type}.collection    # Collection type marker file
├── plugins/                         # Plugin DLLs and manifests
│   ├── {PluginName}_{Version}/
│   │   └── manifest.json
│   └── configurations/             # Plugin configuration files
│       └── {PluginName}.xml
├── config/                          # Application configuration
│   ├── system.xml                   # Core server settings
│   ├── encoding.xml                 # Transcoding settings
│   ├── network.xml                  # Network/binding config
│   ├── database.json                # Database provider config
│   ├── logging.json                 # Log settings
│   ├── migrations.xml               # Migration state tracking
│   ├── users/                       # Per-user configuration
│   └── ScheduledTasks/              # Task schedules
├── cache/
│   ├── images/                      # Resized image cache
│   └── transcodes/                  # Temporary transcode files
│       └── .jellyfin-transcode      # Marker file
└── log/                             # Server log files
```

### Configuration Files (XML/JSON)

Configuration is managed by `BaseConfigurationManager` (`Emby.Server.Implementations/Configuration/ServerConfigurationManager.cs`). Named configuration sections are stored as individual XML files:

| File | Key | Description |
|---|---|---|
| `system.xml` | (root) | Core server options (server name, ports, library scan behavior) |
| `encoding.xml` | `encoding` | FFmpeg options, hardware acceleration, transcoding paths |
| `network.xml` | `network` | Bind addresses, base URL, HTTPS, remote access |
| `database.json` | `database` | Database provider type and options |
| `migrations.xml` | `migrations` | Completed migration tracking |
| `logging.json` | - | Serilog/logging configuration |

Configuration is loaded via `GetConfiguration(key)` which reads `{ConfigurationDirectoryPath}/{key}.xml`, deserializes with XML, and returns a typed instance. Missing files produce default instances.

### Media Library Definitions

Libraries (virtual folders) are defined as directories under `{ProgramDataPath}/root/default/`. Each library directory contains:

- **`options.xml`** - Serialized `LibraryOptions` including:
  - `PathInfos[]` - Array of media source paths on disk
  - Metadata provider preferences and ordering
  - Image fetcher preferences
  - Subtitle fetcher configuration
  - Real-time monitoring toggle
  - Trickplay extraction settings
- **`{type}.collection`** - Empty marker file where `{type}` is one of: `movies`, `tvshows`, `music`, `musicvideos`, `homevideos`, `photos`, `books`, `mixed`

The actual media files remain at their original locations on disk; Jellyfin does not copy or move them.

### Metadata on Disk

Internal metadata is stored at `{ProgramDataPath}/metadata/` organized by entity type. This includes downloaded images for people, artists, genres, studios, and years. Per-item metadata images are referenced by path in the `BaseItemImageInfos` database table.

NFO files and other sidecar metadata may also exist alongside media files depending on library settings (`MetadataSavers` in `LibraryOptions`).

### Image Cache

Processed (resized) images are cached at `{CachePath}/images/`. Original images are stored either alongside media files, in the metadata directory, or downloaded to the image cache. The database stores the path, dimensions, and blurhash for each image.

### Trickplay Files

Trickplay seek-preview thumbnail grids are stored at `{DataPath}/trickplay/`. The `TrickplayInfos` database table stores the associated metadata (dimensions, interval, tile layout). These files can optionally be saved alongside media when `SaveTrickplayWithMedia` is enabled in library options.

### Transcoding Temporary Files

Transcoding temp files are stored at `{CachePath}/transcodes/` by default, or at a custom path set via `EncodingOptions.TranscodingTempPath`. A `.jellyfin-transcode` marker file identifies the directory.

The `DeleteTranscodeFileTask` scheduled task runs at startup and every 24 hours to clean up files older than 1 day.

### Plugin Storage

- **Binaries**: `{ProgramDataPath}/plugins/{PluginName}_{Version}/` containing DLLs and a `manifest.json`
- **Configuration**: `{ProgramDataPath}/plugins/configurations/{PluginName}.xml`

Built-in plugin configurations include:
- `Jellyfin.Plugin.Tmdb.xml` - TMDB provider settings
- `Jellyfin.Plugin.AudioDb.xml` - AudioDB provider settings
- `Jellyfin.Plugin.MusicBrainz.xml` - MusicBrainz provider settings
- `Jellyfin.Plugin.Omdb.xml` - OMDB provider settings
- `Jellyfin.Plugin.StudioImages.xml` - Studio image provider settings

---

## Migrations

### Legacy Databases

Jellyfin historically used multiple separate SQLite databases. These have been consolidated into the single `jellyfin.db`:

| Legacy File | Contained | Migration Routine |
|---|---|---|
| `library.db` | Media items, chapters, streams, people | `MigrateLibraryDb` |
| `users.db` | User accounts | `MigrateUserDb` |
| `authentication.db` | Devices, API keys | `MigrateAuthenticationDb` |
| `display-preferences.db` | Display preferences | `MigrateDisplayPreferencesDb` |

### Migration Routines

Migrations are located in `Jellyfin.Server/Migrations/Routines/` and are executed by `JellyfinMigrationService` in three stages:

1. **PreInitialisation** - Legacy database migrations (runs before app services start)
2. **CoreInitialisation** - Database schema setup via EF Core migrations
3. **AppInitialisation** - Application-level data fixes

Key migration routines:

| Routine | Purpose |
|---|---|
| `MigrateLibraryDb` | Migrate items from legacy `library.db` |
| `MigrateLibraryUserData` | Migrate per-user item data |
| `MigrateUserDb` | Migrate users from legacy `users.db` |
| `MigrateAuthenticationDb` | Migrate devices/keys from `authentication.db` |
| `MigrateDisplayPreferencesDb` | Migrate display preferences |
| `MigrateActivityLogDb` | Migrate activity logs |
| `MigrateKeyframeData` | Migrate keyframe information |
| `MigrateEncodingOptions` | Migrate encoding config to `encoding.xml` |
| `MoveTrickplayFiles` | Reorganize trickplay file layout |
| `MoveExtractedFiles` | Move extracted subtitle/attachment files |
| `FixAudioData` | Correct audio metadata |
| `FixDates` | Fix date format inconsistencies |
| `RemoveDuplicateExtras` | Clean up duplicate extra content |
| `RefreshCleanNames` | Regenerate sanitized sort names |

Safety backups are created before destructive migrations at `{DataPath}/SQLiteBackups/{timestamp}_jellyfin.db`.

Completed migrations are tracked in `{ConfigurationDirectoryPath}/migrations.xml`.

---

## Backup and Restore

Full system backups are managed by `BackupService` (`Jellyfin.Server.Implementations/FullSystemBackup/BackupService.cs`).

**Backup location**: `{DataPath}/backups/jellyfin-backup-{timestamp}.zip`

**Backup contents** (controlled by `BackupOptions`):

| Component | Always Included | Optional Flag |
|---|---|---|
| Configuration files (XML/JSON) | Yes | - |
| User configurations | Yes | - |
| Scheduled task configs | Yes | - |
| Root folder/library definitions | Yes | - |
| Collections | Yes | - |
| Playlists | Yes | - |
| Database tables (as JSON) | Yes | `Database` |
| Subtitles | No | `Subtitles` |
| Trickplay images | No | `Trickplay` |
| Internal metadata | No | `Metadata` |

Each backup ZIP contains a `manifest.json` with:

```json
{
  "ServerVersion": "10.x.x",
  "BackupEngineVersion": "0.2.0",
  "DateCreated": "2025-01-01T00:00:00Z",
  "DatabaseTables": ["Users", "BaseItems", ...],
  "Options": { "Metadata": false, "Trickplay": false, ... }
}
```

Backups require at least 5 GB of free disk space. Restoration validates version compatibility and triggers a server restart upon completion.

---

## Data Access Patterns

Data access follows a **repository pattern** using `IDbContextFactory<JellyfinDbContext>` for scoped context creation:

```
Controller / Service
    └── Repository (IXRepository)
        └── IDbContextFactory<JellyfinDbContext>
            └── JellyfinDbContext (pooled, short-lived)
                └── SQLite via EF Core
```

Key repositories in `Jellyfin.Server.Implementations/`:

| Repository | Responsibility |
|---|---|
| `BaseItemRepository` | Media item CRUD, querying, filtering |
| `MediaStreamRepository` | Audio/video/subtitle stream metadata |
| `PeopleRepository` | Person records and item associations |
| `ChapterRepository` | Chapter markers |
| `MediaAttachmentRepository` | File attachment streams |
| `KeyframeRepository` | Keyframe position data |

Repositories use explicit transactions for multi-step operations and call `SaveChanges()` / `SaveChangesAsync()` through the context, which routes through the configured locking behavior.
