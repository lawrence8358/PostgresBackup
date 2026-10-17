# Linux 離線包軟體來源與 SHA-256

查核日期：2026-10-17。以下是來源與授權記錄，操作請直接使用同資料夾 README.md。

| 軟體 | 版本與來源 | 本地檔案 |
| --- | --- | --- |
| Docker static | 29.9.1；https://download.docker.com/linux/static/stable/x86_64/docker-29.9.1.tgz | software/docker-29.9.1.tgz |
| PostgreSQL 官方映像檔 | postgres:18.6；linux/amd64；image digest sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280；https://hub.docker.com/_/postgres | software/postgres-18.6.tar（docker save 完整映像檔，內含OS libraries） |
| PostgresBackup CLI | v1.0.0、本專案原始碼、net10.0、linux-x64、self-contained（含SQLite native library） | software/cli/ |
| BusyBox static | 1.35.0；https://busybox.net/downloads/binaries/1.35.0-x86_64-linux-musl/busybox | software/busybox |
| BusyBox GPL-2.0 原始碼 | https://busybox.net/downloads/busybox-1.35.0.tar.bz2 | software/busybox-1.35.0-source.tar.bz2 |
| 授權與第三方聲明 | Docker／PostgreSQL／BusyBox／.NET／PostgresBackup；Docker LICENSE/NOTICE 為官方 moby master 原文 | software/licenses/（含 NuGet 套件版本、原始 nuspec 與授權） |

BusyBox 已附同版本官方完整 source、GPL、官方 binary 的 build config 與 linker script：來源 https://busybox.net/downloads/binaries/1.35.0-x86_64-linux-musl/ （本地 software/busybox-1.35.0-build.config 與 .ldscript）。

## 檔案 SHA-256

```text
6e123e7f3202a8c1e9b1f94d8941580a25135382b99e8d3e34fb858bba311348  software/busybox
bc04f0222c39f0edf35eeb034fae3dc35a6e38a048c259d4c977ba915b9d57a6  software/busybox-1.35.0-build.config
e88a48d4f772a94c6e7ec9bcc26a102eb4ed82eee71de77cb18d51abfd5aa481  software/busybox-1.35.0-build.ldscript
faeeb244c35a348a334f4a59e44626ee870fb07b6884d68c10ae8bc19f83a694  software/busybox-1.35.0-source.tar.bz2
c63a20a176b0b8d127a9a114c56863c83cf320081949419fa71842ac7689f972  software/cli/PostgresBackup.Core.pdb
965400c23fde16a19d000c1c6062af577f048410f85253a793f569d5520daaad  software/cli/libe_sqlite3.so
8e873baff4bf7edb3754c3003c6d1b720dce8dc70990a4043f9cb17eb988c309  software/cli/pgbackup
0f8c99d1a521803254e001f6c40bf4566e98f4219a8fbb60da0867425660743b  software/cli/pgbackup.pdb
c158ecc55adb3afb01bbd3539ed3b997d20c26dbdc418080506cc1bf5243ff57  software/docker-29.9.1.tgz
bbfc9843646d483c334664f651c208b9839626891d8f17604db2146962f43548  software/licenses/BusyBox-LICENSE.txt
7c87873291f289713ac5df48b1f2010eb6963752bbd6b530416ab99fc37914a8  software/licenses/Docker-LICENSE.txt
a249c4197359eee43031d16cecb172326f92ee7b6701af99dfe5c3e5b3d5eca9  software/licenses/Docker-NOTICE.txt
0e027c13e389ffca97a3ad4053372b747c062d33345c9915ecb060e33ee90643  software/licenses/Npgsql-LICENSE.txt
e2a496538a0a1844315aaa638f7f8d1ecd9ac18aee3b97a9098faead5401f642  software/licenses/NuGet-packages.txt
ff0b98c862d7afabbfa2076cece016b0ff62737bea0558098b2b2ea352d3e26e  software/licenses/PostgreSQL-copyright.txt
b86877974de39615249472199ac33117ea8ffcf23d5e52d649f5b62674ef93de  software/licenses/PostgresBackup-LICENSE.txt
cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30  software/licenses/SQLitePCLRaw-LICENSE.txt
7f6839a61ce892b79c6549e2dc5a81fdbd240a0b260f8881216b45b7fda8b45d  software/licenses/dotnet-LICENSE.txt
1271701f435f7fe4aa81dc7e273ca80b6391b73580ee20b35a956052c95de4cf  software/licenses/dotnet-ThirdPartyNotices.txt
8df9424d472c0d2693f11a287e20f5549dfa2938973d5c4ac8004c9258de3203  software/licenses/nuget/Microsoft.Data.Sqlite-10.0.0/Microsoft.Data.Sqlite.nuspec
ab42befb78a634e50af1ce4e1b6ad39ae068f6dd7f822fba10abe117fef92814  software/licenses/nuget/Microsoft.Data.Sqlite.Core-10.0.0/Microsoft.Data.Sqlite.Core.nuspec
34693997ad3738ce76702051669f5380fb7c6614804804102b10ec7a704a6ca1  software/licenses/nuget/Microsoft.Extensions.Configuration-10.0.0/Microsoft.Extensions.Configuration.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Configuration-10.0.0/THIRD-PARTY-NOTICES.TXT
d8124af5988bf172eff7d8469a0e16bf7c5253530440b4ca6ba18a4890740f9c  software/licenses/nuget/Microsoft.Extensions.Configuration.Abstractions-10.0.0/Microsoft.Extensions.Configuration.Abstractions.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Configuration.Abstractions-10.0.0/THIRD-PARTY-NOTICES.TXT
10a186eb097268352bfaa01d9e78256557f8c3ef7fc0a370d05fc8952b03dca4  software/licenses/nuget/Microsoft.Extensions.Configuration.Binder-10.0.0/Microsoft.Extensions.Configuration.Binder.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Configuration.Binder-10.0.0/THIRD-PARTY-NOTICES.TXT
0f3b7bc57edb81ae05a1c59b0828d99cd2c1bd6b5de34dcce9b7519081461904  software/licenses/nuget/Microsoft.Extensions.DependencyInjection-10.0.0/Microsoft.Extensions.DependencyInjection.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.DependencyInjection-10.0.0/THIRD-PARTY-NOTICES.TXT
9e13d9b543405e86955f6b047a1d7680684fa37f4ec094f4bad6e33ba775c0db  software/licenses/nuget/Microsoft.Extensions.DependencyInjection.Abstractions-10.0.0/Microsoft.Extensions.DependencyInjection.Abstractions.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.DependencyInjection.Abstractions-10.0.0/THIRD-PARTY-NOTICES.TXT
a98260030de003de5c14cb84e56a8bbc9937df42e3174e627e683fe64beace23  software/licenses/nuget/Microsoft.Extensions.Logging-10.0.0/Microsoft.Extensions.Logging.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Logging-10.0.0/THIRD-PARTY-NOTICES.TXT
126d33e134dcbe6a65a969b93e2faa3dcf73dbc460c307e783b4f719eed12688  software/licenses/nuget/Microsoft.Extensions.Logging.Abstractions-10.0.0/Microsoft.Extensions.Logging.Abstractions.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Logging.Abstractions-10.0.0/THIRD-PARTY-NOTICES.TXT
d19e44fe80416905850f91045907bcfb23de39a8cb9b96b9d6a55a795ddf3807  software/licenses/nuget/Microsoft.Extensions.Logging.Configuration-10.0.0/Microsoft.Extensions.Logging.Configuration.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Logging.Configuration-10.0.0/THIRD-PARTY-NOTICES.TXT
00a1d908bdc7a4d7534dfad36231c093364b191eb49a3956729e40cf81574fdf  software/licenses/nuget/Microsoft.Extensions.Logging.Console-10.0.0/Microsoft.Extensions.Logging.Console.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Logging.Console-10.0.0/THIRD-PARTY-NOTICES.TXT
7209d42974a0de402e1eec4c5099c48c6f1f83d08b36016c710e8dc23c9e1eff  software/licenses/nuget/Microsoft.Extensions.Options-10.0.0/Microsoft.Extensions.Options.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Options-10.0.0/THIRD-PARTY-NOTICES.TXT
30ba601042f9ea75eef8de3826fa8fbdd41f1bd935c38c76dc0ebc74d828cb07  software/licenses/nuget/Microsoft.Extensions.Options.ConfigurationExtensions-10.0.0/Microsoft.Extensions.Options.ConfigurationExtensions.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Options.ConfigurationExtensions-10.0.0/THIRD-PARTY-NOTICES.TXT
01892e3b694e60d0fbbc2920f600a3344be647bdf9c046f7acf69272e4fa968f  software/licenses/nuget/Microsoft.Extensions.Primitives-10.0.0/Microsoft.Extensions.Primitives.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.Extensions.Primitives-10.0.0/THIRD-PARTY-NOTICES.TXT
71b6ff316b26deec07c25f0fb0de7b4d1e0648833f8d812350b79ed94b288cbe  software/licenses/nuget/Microsoft.NET.ILLink.Tasks-10.0.1/Microsoft.NET.ILLink.Tasks.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/Microsoft.NET.ILLink.Tasks-10.0.1/THIRD-PARTY-NOTICES.TXT
6a7b5f809e779060c2ba2a2600c0c7f7fa8ada5f20ac816fac682c7c95466afa  software/licenses/nuget/Npgsql-9.0.3/Npgsql.nuspec
f8204aad2733babf578b5935a7c75acac78eafd22eb7467cef25d305b7e06a10  software/licenses/nuget/SQLitePCLRaw.bundle_e_sqlite3-2.1.11/SQLitePCLRaw.bundle_e_sqlite3.nuspec
6561e062a5e659ded08feb24f13b70606678c1912bb655a3c24599a75490d4ae  software/licenses/nuget/SQLitePCLRaw.core-2.1.11/SQLitePCLRaw.core.nuspec
fba9ca3b4fc13b6e2c118a898d760750046b099af3a56740c2017005e71ad1e4  software/licenses/nuget/SQLitePCLRaw.lib.e_sqlite3-2.1.11/SQLitePCLRaw.lib.e_sqlite3.nuspec
55c4de0d90d48c4b2365ff63ce939a7d87346dcb4d932830d28fc2c316439f07  software/licenses/nuget/SQLitePCLRaw.provider.e_sqlite3-2.1.11/SQLitePCLRaw.provider.e_sqlite3.nuspec
833cd86779d2d081abc58d93a63463243b9176924c456e0f74707adbb8ec9299  software/licenses/nuget/System.CommandLine-2.0.3/System.CommandLine.nuspec
6f2769743da544fc8bcca74ffd58b319185aa24964cd737ad91c08473f159e6c  software/licenses/nuget/System.Security.Cryptography.ProtectedData-10.0.0/System.Security.Cryptography.ProtectedData.nuspec
6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21  software/licenses/nuget/System.Security.Cryptography.ProtectedData-10.0.0/THIRD-PARTY-NOTICES.TXT
bd45be3144eb738270d9ef4573da609b85a9849a2b6b0a137069bbe907704ea7  software/postgres-18.6.tar
```
