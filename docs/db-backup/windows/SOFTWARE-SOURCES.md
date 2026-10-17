# Software sources

Metadata only; operation instructions are in README.md.

| Local file | Origin | SHA256 |
| --- | --- | --- |
| software/postgresql-18.6-windows-x64.zip | https://www.enterprisedb.com/download-postgresql-binaries (complete original PostgreSQL 18.6 Windows x64 archive) | FBE23DA234EE31547BF8A36D29DFD81E82B849DF2D2B78D2EECB43D360252F8C |
| software/PostgresBackup-CLI-windows-x64.zip | https://github.com/lawrence8358/PostgresBackup ; v1.0.0 self-contained win-x64 publish with native libraries bundled for self extraction, all published files and notices | 5E652ADB7423201E3E3B71421F6B35EBCDDFF002D59FA9008B38A73654E9C056 |
| software/vc_redist.x64.exe | https://aka.ms/vc14/vc_redist.x64.exe ; Microsoft signature Valid | 843068991DAAA1F73AD9F6239BCE4D0F6A07A51F18C37EA2A867E9BECA71295C |

PostgreSQL archive retains all 21968 entries, including server_license.txt and all third-party notices. No files were selected or removed.
.NET runtime MIT license and ThirdPartyNotices.txt: software/licenses/dotnet. NuGet notices and package metadata: software/licenses/nuget. Project MIT: LICENSE and inside CLI ZIP.
Visual C++ installer retains its embedded Microsoft license. Runtime installer version:
14.51.36247.0
PE import-table inspection: initdb.exe, postgres.exe, pg_dump.exe and libpq.dll directly import VCRUNTIME140.dll. Windows built-in UCRT imports also present.
Acceptance host already has Microsoft runtime 14.50.35719.0 in System32. Clean PATH does not prove an OS without that runtime; missing-runtime machines must install bundled vc_redist.x64.exe as administrator.
No Docker, WSL, SDK or Git executable is used during acceptance. Windows PowerShell 5.1, Task Scheduler and Windows APIs are OS components.
