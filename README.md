[English](README.md) | [繁體中文](README.zh-TW.md)

# PostgresBackup

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-12~18-336791?logo=postgresql)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green.svg)

A PostgreSQL backup and restore tool built on the official `pg_dump` / `pg_restore` / `psql` binaries — with a modern WPF desktop UI, a scriptable CLI, and a mandatory pre-restore snapshot so a restore can never be the last thing that happens to your data.

No custom dump parser. No reinvented wire protocol. Just the official tools, driven safely.

## Table of Contents

- [Why PostgresBackup?](#why-postgresbackup)
- [Features](#features)
- [Screenshots](#screenshots)
- [Getting Started](#getting-started)
- [Installing the PostgreSQL Client Tools](#installing-the-postgresql-client-tools)
- [CLI Usage](#cli-usage)
- [WPF Desktop Application](#wpf-desktop-application)
- [Scheduled Backups](#scheduled-backups)
- [Where Data Is Stored](#where-data-is-stored)
- [Building from Source](#building-from-source)
- [Documentation](#documentation)
- [License](#license)

## Why PostgresBackup?

`pg_dump` and `pg_restore` are excellent, and nothing should replace them. What they lack is the operational layer around them: a safe default for destructive restores, a place to keep connection profiles without plaintext passwords, an audit trail of what was backed up and when, and a UI a colleague can use without memorising twenty flags.

Third-party tools that re-implement dumping in managed code trade that away — the moment a data type round-trips through someone's own parser, fidelity becomes a maintenance promise rather than a guarantee.

PostgresBackup takes the other path:

- **Official tools only:** every backup is a real `pg_dump` invocation; every restore is `pg_restore` or `psql`. The output files are ordinary PostgreSQL dumps, readable by any standard tooling.
- **Safe by default:** a restore builds a defensive snapshot of the target database *first*. If the snapshot fails, the restore does not run.
- **No plaintext credentials:** passwords live in Windows Credential Manager, and reach the child process through the `PGPASSWORD` environment variable — never on a command line a process monitor can read.
- **Auditable:** backups, restores and snapshots are all written to a local SQLite history you can search and act on.

## Features

- Backup via official `pg_dump` — Custom (`-Fc`) or Plain SQL (`-Fp`) format
- Backup modes: schema + data, schema only, data only
- Backup scopes: full database, specific schemas, or specific tables
- Restore via `pg_restore` (`.dump`) or `psql` (`.sql`), in normal / clean-and-recreate / data-only modes
- **Pre-restore snapshot** — a defensive full backup taken before any destructive restore (on by default)
- Typed-confirmation guard in the UI before a restore can be executed
- Client tool auto-detection with client/server version compatibility checking
- Connection profiles with passwords stored in Windows Credential Manager
- Immutable SQLite audit history, searchable and filterable by operation type
- Live streaming of the underlying tool's output in both the UI and the CLI
- CLI (`pgbackup`) for Task Scheduler, CI/CD and automation
- Runtime language switching (English / 繁體中文)

## Screenshots

### Settings — environment check and connection profiles
![Settings](docs/screenshots/en/demo_wpf_settings.png)

### Backup
![Backup](docs/screenshots/en/demo_wpf_backup.png)

### Restore — pre-restore snapshot and typed confirmation
![Restore](docs/screenshots/en/demo_wpf_restore.png)

### History — immutable audit trail
![History](docs/screenshots/en/demo_wpf_history.png)

### Live log
![Log](docs/screenshots/en/demo_wpf_log.png)

## Getting Started

### Prerequisites

- Windows 10 / 11 or Windows Server 2019+
- PostgreSQL official client tools (`pg_dump`, `pg_restore`, `psql`) — see [below](#installing-the-postgresql-client-tools)
- PostgreSQL 12 ~ 18.x server

No .NET runtime is required: both downloads are self-contained single-file executables.

### Download

Grab the latest release from the [Releases](https://github.com/lawrence8358/PostgresBackup/releases) page:

| Package | Contents |
|---------|----------|
| `PostgresBackup-wpf-win-x64-v<version>.zip` | `PostgresBackup.Wpf.exe` — the desktop application |
| `PostgresBackup-cli-win-x64-v<version>.zip` | `pgbackup.exe` — the command-line tool |

Each archive holds a single self-contained executable plus the LICENSE and docs. Unzip anywhere and run — nothing is written to the registry.

## Installing the PostgreSQL Client Tools

PostgresBackup calls the official binaries, so they must be present on the machine. The client major version must be **greater than or equal to** the server's — `pg_dump` 16 cannot dump a PostgreSQL 18 server.

Everything referenced here is covered by the **PostgreSQL License** (an OSI-approved permissive licence comparable to MIT/BSD) and is free for commercial and production use, including inside proprietary systems.

### Option A — portable EDB binaries (recommended)

No installer, no admin rights, no registry writes.

1. Download the **Windows x86-64** ZIP from [PostgreSQL Windows Binaries (EDB)](https://www.enterprisedb.com/download-postgresql-binaries).
2. Extract the `pgsql` folder somewhere stable, e.g. `C:\Tools\pgsql\`.
3. Point PostgresBackup at `C:\Tools\pgsql\bin`, via `--pg-bin-path` or the Settings page.

### Option B — winget

```powershell
winget install PostgreSQL.PostgreSQL.18
```

The tools land in `C:\Program Files\PostgreSQL\18\bin` and are added to `PATH`, so PostgresBackup finds them automatically.

### Verifying

```powershell
pgbackup check-tools --pg-bin-path "C:\Tools\pgsql\bin"
```

```text
================================================================================
  PostgreSQL Client Tools Diagnostic Report
================================================================================
  Status     : [ READY ]
  Source     : CustomPath
  pg_dump    : C:\Tools\pgsql\bin\pg_dump.exe
  pg_restore : C:\Tools\pgsql\bin\pg_restore.exe
  psql       : C:\Tools\pgsql\bin\psql.exe
  Version    : 18.6
================================================================================
```

## CLI Usage

```bash
# Diagnose the local tool installation
pgbackup check-tools

# Diagnose, and also verify client/server version compatibility
pgbackup check-tools -H localhost -d my_database -u postgres -p "secret"

# Full database backup, Custom format
pgbackup backup -H localhost -d my_database -u postgres -p "secret" -f custom -o "D:\Backups"

# Schema-only backup, as a readable .sql script
pgbackup backup -H localhost -d my_database -u postgres -p "secret" -f plain -m schema -o "D:\Backups"

# Back up two schemas only
pgbackup backup -H localhost -d my_database -u postgres -p "secret" -n public -n hangfire -o "D:\Backups"

# Restore — takes a pre-restore snapshot automatically, prompts before overwriting
pgbackup restore -f "D:\Backups\my_database_20260917235837.dump" -H localhost -d my_database -u postgres -p "secret"

# Restore unattended (for scripts): skip the prompt, keep the snapshot
pgbackup restore -f "D:\Backups\my_database_20260917235837.dump" -H localhost -d my_database -u postgres -p "secret" --yes
```

### Command Reference

| Command | Description |
|---------|-------------|
| `check-tools` | Report client tool paths, versions and readiness; optionally check server compatibility |
| `backup` | Run a backup with `pg_dump` |
| `restore` | Run a restore with `pg_restore` / `psql`, preceded by a safety snapshot |

**Connection options** — accepted by all three commands:

| Option | Alias | Description |
|--------|-------|-------------|
| `--profile` | | Use a saved connection profile, by name or id |
| `--host` | `-H` | Server host |
| `--port` | `-P` | Server port (default `5432`) |
| `--database` | `-d` | Database name |
| `--username` | `-u` | Username |
| `--password` | `-p` | Password (handed to the child process via `PGPASSWORD`, never on its command line) |
| `--pg-bin-path` | | Directory containing the official client tools |

Explicit options override the values taken from `--profile`.

**`backup` options**

| Option | Alias | Description |
|--------|-------|-------------|
| `--format` | `-f` | `custom` (`-Fc`, default) or `plain` (`-Fp`) |
| `--mode` | `-m` | `all` (schema + data), `schema`, or `data` |
| `--schema` | `-n` | Restrict to a schema; repeatable |
| `--table` | `-t` | Restrict to a table, e.g. `public.Quote`; repeatable |
| `--output-dir` | `-o` | Output directory (default: `Documents\PostgresBackups`) |
| `--output-file` | | Explicit output file path, overriding the generated name |
| `--log` | | Also append the run log to this file |

Generated file names follow `{database}_{yyyyMMddHHmmss}.dump`, or `.sql` for plain format.

**`restore` options**

| Option | Alias | Description |
|--------|-------|-------------|
| `--file` | `-f` | **Required.** Source backup file (`.dump` or `.sql`) |
| `--mode` | `-m` | `normal` (default), `clean` (`--clean --create`), or `data` (`--data-only`) |
| `--no-snapshot` | | Disable the pre-restore safety snapshot |
| `--yes` | `-y` | Skip the interactive destructive-operation confirmation |
| `--log` | | Also append the run log to this file |

> **`--no-snapshot` removes your undo.** The snapshot is what lets you get back to the pre-restore state when the dump turns out to be the wrong one. Use it only when the target database is disposable.

**`check-tools` options**

| Option | Alias | Description |
|--------|-------|-------------|
| `--connection-string` | `-s` | Full connection string, used for the server compatibility check |
| `--json` | | Emit the report as JSON, for scripted health checks |

The CLI exits `0` on success and non-zero on failure, so it composes normally with scripts and CI steps.

## WPF Desktop Application

Five pages, in the order you would use them:

1. **Settings** — detect or point at the client tools, then create connection profiles. Passwords go to Windows Credential Manager; the profile file itself holds no secrets. "Test connection" also reports client/server version compatibility.
2. **Backup** — pick a profile, choose format / mode / scope, and watch the output file name preview update live. Execution streams straight to the log.
3. **Restore** — pick a backup file and a target. The pre-restore snapshot is checked by default. The execute button stays locked until you retype the target database name and acknowledge the overwrite risk.
4. **History** — every backup, restore and snapshot, filterable by type or database name, with "reveal in Explorer" and one-click "restore this file".
5. **Log** — the live `pg_dump` / `pg_restore` stream, including per-table progress, with copy and clear.

The language switcher (English / 繁體中文) at the bottom of the sidebar applies immediately, with no restart.

## Scheduled Backups

The CLI is built for unattended operation. A complete PowerShell scheduling script — retention policy, log rotation and `schtasks` registration — is in the [User Manual](docs/USER_MANUAL.md#5-cli-自動化排程備份實戰指南-sop). The short version:

```powershell
schtasks /Create /TN "PostgresBackup_Daily" /TR "powershell.exe -ExecutionPolicy Bypass -File C:\Scripts\backup_task.ps1" /SC DAILY /ST 02:00 /RU "SYSTEM" /F
```

## Where Data Is Stored

| What | Location |
|------|----------|
| Connection profiles | `%APPDATA%\PostgresBackup\connections.json` |
| Passwords | Windows Credential Manager |
| Audit history | `%LOCALAPPDATA%\PostgresBackup\history.db` (SQLite) |
| Default backup output | `%USERPROFILE%\Documents\PostgresBackups` |

Both applications share all four, so a profile created in the UI is immediately usable from `pgbackup --profile`.

## Building from Source

Requires the .NET 10 SDK.

```powershell
dotnet build PostgresBackup.sln
dotnet test PostgresBackup.sln
```

To produce the release artifacts — self-contained, single-file, no runtime needed on the target machine:

```powershell
.\build.ps1
```

This writes to `dist/` — both the release archives and the unpacked executables, so you can smoke-test a build before publishing it:

```
dist/
├── cli/pgbackup.exe
├── wpf/PostgresBackup.Wpf.exe
├── PostgresBackup-cli-win-x64-v<version>.zip
└── PostgresBackup-wpf-win-x64-v<version>.zip
```

The version is read from the project files, so bumping `<Version>` is all that is needed for the next release. `.\build.ps1 -Version 1.1.0` overrides it for a one-off build, and `-Runtime win-arm64` targets a different architecture.

## Documentation

- [User Manual (繁體中文)](docs/USER_MANUAL.md) — full walkthrough, tool installation SOP, scheduling guide and FAQ
- [Product Context](PRODUCT.md) — vision, target users and design guardrails
- [Domain Model](CONTEXT.md) — project terminology
- [Design Notes](DESIGN.md) — architecture

## License

MIT — see [LICENSE](LICENSE).

PostgresBackup invokes the PostgreSQL client tools but does not redistribute them; those are separately licensed under the PostgreSQL License.
