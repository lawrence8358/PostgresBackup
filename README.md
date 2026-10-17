[English](README.md) | [繁體中文](README.zh-TW.md)

# PostgresBackup v1.0.0

A PostgreSQL backup and restore tool with a Windows desktop app and a Windows / Linux CLI. It uses the official `pg_dump`, `pg_restore`, and `psql` tools, and takes a safety snapshot before restoring by default.

## DB backup bundles

Each folder includes its own SOP, CLI, required software, and scripts:

- [Windows CLI scheduling and restore](docs/db-backup/windows/README.md)
- [Linux CLI scheduling and restore](docs/db-backup/linux/README.md)

## Get started

Choose a walkthrough. Each includes setup, screenshots, and SQL checks after restoration. The guides are in Traditional Chinese.

| Task | Guide |
| --- | --- |
| Back up and restore through the desktop app | [Desktop](docs/sop/desktop.md) |
| Schedule Windows backups and restore when needed | [Windows CLI](docs/sop/windows-cli.md) |
| Back up and restore on Linux | [Linux CLI](docs/sop/linux-cli.md) |
| Practise with sample data | [Create a test database](docs/sop/README.md) |
| Test backup and restore with 1,000,000 orders | [Load test](docs/sop/stress-test.md) |

![A completed desktop backup](docs/sop/images/desktop-02-backup.png)

## Prepare the tools

- Download the complete [Windows](https://github.com/lawrence8358/PostgresBackup/releases/latest/download/PostgresBackup-Windows-x64.zip) or [Linux](https://github.com/lawrence8358/PostgresBackup/releases/latest/download/PostgresBackup-Linux-x64.zip) CLI bundle, or build the [source](https://github.com/lawrence8358/PostgresBackup).
- For a Windows source build, also prepare the [PostgreSQL client tools](https://www.enterprisedb.com/download-postgresql-binaries). Extract the entire ZIP to `C:\Tools`, keeping every file and subfolder inside `pgsql`. Set the client tool directory to `C:\Tools\pgsql\bin`. The client major version must be at least the server major version.
- To build, install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then run this from the project folder:

```powershell
.\build.ps1
```

The Windows app is `dist\wpf\PostgresBackup.Wpf.exe`; the CLI is `dist\cli\pgbackup.exe`. See the Linux guide for Linux builds.

## Features and operating notes

- Custom (`.dump`) and SQL (`.sql`) backups, covering a database, schemas, or tables.
- Schema and data, schema-only, or data-only backups; normal, clean-and-recreate, or data-only restores.
- Desktop connection profiles, tool version checks, operation history, and English / Traditional Chinese UI.
- Stop application writes before restoring, keep the safety snapshot, and verify the restored data with SQL.
- Desktop and CLI profiles are separate. Windows `--profile` uses an encrypted store requiring Administrator or SYSTEM access. Each guide explains password files and scheduling limitations.

## Reference

- [CLI commands, security, and troubleshooting](docs/USER_MANUAL.md)
- [SYSTEM scheduling scripts and parameters](scripts/README.md)
- [Product](PRODUCT.md) · [Domain model](CONTEXT.md) · [Design](DESIGN.md)

Development checks: `dotnet build PostgresBackup.sln` and `dotnet test PostgresBackup.sln`.

Licensed under the [MIT License](LICENSE).
