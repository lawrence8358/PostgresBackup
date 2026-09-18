<#
================================================================================
 PostgresBackup — Release Build Script
--------------------------------------------------------------------------------
 產出自我包含 (self-contained)、單一檔案 (single-file) 之可攜式發行版本。
 目標機器無須預先安裝 .NET 執行階段。

 輸出結構 (dist/)：
   dist/
   |-- cli/pgbackup.exe                              <- 可直接執行測試
   |-- wpf/PostgresBackup.Wpf.exe                    <- 可直接執行測試
   |-- PostgresBackup-cli-win-x64-v<version>.zip         <- 發行用封裝
   `-- PostgresBackup-wpf-win-x64-v<version>.zip

 用法:
   .\build.ps1                      # 讀取專案檔版本號，建置 cli + wpf (win-x64)
   .\build.ps1 -Version 1.1.0       # 覆寫版本號
   .\build.ps1 -Target cli          # 僅建置 CLI
   .\build.ps1 -Runtime win-arm64   # 建置其他架構
   .\build.ps1 -SkipTests           # 略過單元測試
================================================================================
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'cli', 'wpf')]
    [string]$Target = 'all',

    [string]$Runtime = 'win-x64',

    [string]$Configuration = 'Release',

    [string]$Version,

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$distDir = Join-Path $root 'dist'

function Write-Step([string]$message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Write-Done([string]$message) {
    Write-Host "    $message" -ForegroundColor Green
}

function Get-ProjectVersion([string]$csprojPath) {
    $xml = [xml](Get-Content -Path $csprojPath -Raw)
    $node = $xml.Project.PropertyGroup.Version | Where-Object { $_ }
    if (-not $node) {
        throw "在 $csprojPath 中找不到 <Version> 屬性，請以 -Version 明確指定版本號。"
    }
    return ([string]$node).Trim()
}

# ---------------------------------------------------------------- 專案定義 ----
$projects = @(
    [pscustomobject]@{
        Key      = 'cli'
        Name     = 'PostgresBackup.Cli'
        Csproj   = Join-Path $root 'src\PostgresBackup.Cli\PostgresBackup.Cli.csproj'
        ExeName  = 'pgbackup.exe'
    },
    [pscustomobject]@{
        Key      = 'wpf'
        Name     = 'PostgresBackup.Wpf'
        Csproj   = Join-Path $root 'src\PostgresBackup.Wpf\PostgresBackup.Wpf.csproj'
        ExeName  = 'PostgresBackup.Wpf.exe'
    }
)

if ($Target -ne 'all') {
    $projects = $projects | Where-Object { $_.Key -eq $Target }
}

# ------------------------------------------------------------------ 版本號 ----
if (-not $Version) {
    $Version = Get-ProjectVersion $projects[0].Csproj
}

Write-Host ''
Write-Host '================================================================================' -ForegroundColor DarkGray
Write-Host "  PostgresBackup v$Version  —  $Configuration / $Runtime  (self-contained, single-file)" -ForegroundColor White
Write-Host '================================================================================' -ForegroundColor DarkGray

# -------------------------------------------------------------------- 測試 ----
if (-not $SkipTests) {
    Write-Step '執行單元測試'
    dotnet test (Join-Path $root 'PostgresBackup.sln') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "單元測試失敗 (exit code $LASTEXITCODE)，已中止發行建置。"
    }
    Write-Done '所有測試通過'
}
else {
    Write-Host ''
    Write-Host '    (已略過單元測試)' -ForegroundColor Yellow
}

# ------------------------------------------------------------ 清空 dist 目錄 ----
Write-Step "準備輸出目錄: $distDir"
if (Test-Path $distDir) {
    Remove-Item -Path $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
Write-Done '已建立 dist/'

# ------------------------------------------------------------ 發行與打包 ----
$artifacts = @()

foreach ($project in $projects) {
    Write-Step "發行 $($project.Name)"

    # 直接發行至 dist\<key>\，保留可執行檔供實機測試，zip 則另置於 dist\ 根目錄
    $publishDir = Join-Path $distDir $project.Key

    dotnet publish $project.Csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDir `
        --nologo `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version `
        -p:FileVersion=$Version `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:GenerateDocumentationFile=false

    if ($LASTEXITCODE -ne 0) {
        throw "$($project.Name) 發行失敗 (exit code $LASTEXITCODE)。"
    }

    $exePath = Join-Path $publishDir $project.ExeName
    if (-not (Test-Path $exePath)) {
        throw "預期的可執行檔不存在: $exePath"
    }

    # 單一檔案發行仍可能留下偵錯符號，一併清除以保持封裝乾淨
    Get-ChildItem -Path $publishDir -Filter '*.pdb' -Recurse -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    # 隨附授權與說明文件
    Copy-Item -Path (Join-Path $root 'LICENSE') -Destination $publishDir -Force
    Copy-Item -Path (Join-Path $root 'README.md') -Destination $publishDir -Force
    Copy-Item -Path (Join-Path $root 'README.zh-TW.md') -Destination $publishDir -Force
    Copy-Item -Path (Join-Path $root 'docs\USER_MANUAL.md') -Destination $publishDir -Force

    $zipName = "PostgresBackup-$($project.Key)-$Runtime-v$Version.zip"
    $zipPath = Join-Path $distDir $zipName

    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

    $exeSizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
    $zipSizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Done "$($project.ExeName)  ($exeSizeMb MB)  ->  $zipName  ($zipSizeMb MB)"

    $artifacts += [pscustomobject]@{
        Package = $zipName
        SizeMB  = $zipSizeMb
        Exe     = $exePath.Replace($distDir + '\', '')
    }
}

Write-Host ''
Write-Host '================================================================================' -ForegroundColor DarkGray
Write-Host "  建置完成 — 產物位於 $distDir" -ForegroundColor Green
Write-Host '================================================================================' -ForegroundColor DarkGray
Write-Host '  zip 為發行用封裝；Exe 欄位為已解開的可執行檔，可直接執行進行實機測試。' -ForegroundColor White
$artifacts | Format-Table -AutoSize

exit 0
