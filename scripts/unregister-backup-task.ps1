<#
================================================================================
 unregister-backup-task.ps1 — 移除排程備份

 預設只做一件事：**把排程任務移除，其他東西一律保留。**

 備份檔是資料，不是設定。停掉排程跟丟掉既有備份是兩件完全不同的決定，
 所以這支腳本不會自作主張——要刪連線設定、要刪存放區、要刪備份檔，
 都得你明確加上對應的參數。

 先看看會動到什麼（不會真的刪任何東西）：
   .\unregister-backup-task.ps1 -BackupDir "D:\DatabaseBackups\my_database" -DryRun

 只停排程（最常見。備份檔、連線設定都留著）：
   .\unregister-backup-task.ps1

 停排程並清掉連線設定（含其加密密碼）：
   .\unregister-backup-task.ps1 -RemoveProfile -ProfileName "正式環境"

 整套拆乾淨，連備份檔一起刪（**會刪掉資料，刪了拿不回來**）：
   .\unregister-backup-task.ps1 -RemoveProfile -ProfileName "正式環境" `
                                -RemoveStore -RemoveBackups -BackupDir "D:\DatabaseBackups\my_database"

 需要以「系統管理員」身分執行：移除 SYSTEM 排程任務、刪除連線設定
 （寫入 %ProgramData%）都需要管理員權限。

 退出碼：0 = 全部順利 / 1 = 有項目失敗 / 2 = 環境或參數有問題
================================================================================
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'PostgresBackup_Daily',

    # --- 要不要一併移除連線設定 ---------------------------------------------
    [switch]$RemoveProfile,
    [string]$ProfileName,
    [string]$CliPath,                   # pgbackup.exe；-RemoveProfile 時需要

    # --- 要不要一併移除機器範圍存放區 ----------------------------------------
    # 只有在裡面已經沒有任何連線設定時才會刪，除非另外加上 -Force。
    [switch]$RemoveStore,

    # --- 要不要一併刪掉備份檔（危險）-----------------------------------------
    [switch]$RemoveBackups,
    [string]$BackupDir,

    # --- 行為控制 ------------------------------------------------------------
    [switch]$DryRun,                    # 只顯示會做什麼，不實際執行
    [switch]$Yes,                       # 不跳出互動確認（自動化用）
    [switch]$Force                      # 存放區中仍有連線設定時，仍然刪除存放區
)

$ErrorActionPreference = 'Stop'
$script:Failures = @()
$script:Removed = @()
$script:Kept = @()

function Write-Section($text) {
    Write-Host ''
    Write-Host "== $text " -ForegroundColor Cyan -NoNewline
    Write-Host ('=' * [Math]::Max(0, 68 - $text.Length)) -ForegroundColor Cyan
}
function Write-Ok($t)   { Write-Host '[完成] ' -ForegroundColor Green  -NoNewline; Write-Host $t }
function Write-Skip($t) { Write-Host '[略過] ' -ForegroundColor DarkGray -NoNewline; Write-Host $t }
function Write-Warn($t) { Write-Host '[注意] ' -ForegroundColor Yellow -NoNewline; Write-Host $t }
function Write-Bad($t)  { Write-Host '[失敗] ' -ForegroundColor Red    -NoNewline; Write-Host $t; $script:Failures += $t }
function Write-Info($t) { Write-Host "       $t" -ForegroundColor DarkGray }
function Write-Plan($t) { Write-Host '[預演] ' -ForegroundColor Magenta -NoNewline; Write-Host $t }

# 確認一個破壞性動作。-Yes 可略過；-DryRun 一律視為不執行。
function Confirm-Destructive($prompt, $mustType) {
    if ($Yes) { return $true }

    Write-Host ''
    Write-Host $prompt -ForegroundColor Yellow
    Write-Host "若確定執行，請輸入 " -NoNewline
    Write-Host $mustType -ForegroundColor White -NoNewline
    Write-Host ' ：' -NoNewline
    $answer = Read-Host
    if ($answer -ne $mustType) {
        Write-Host '輸入不符，已取消這個項目。' -ForegroundColor DarkGray
        return $false
    }
    return $true
}

# ------------------------------------------------------------------ 前置檢查

Write-Section '前置檢查'

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin   = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin -and -not $DryRun) {
    Write-Bad "目前不是系統管理員身分（$($identity.Name)）。"
    Write-Host ''
    Write-Host '移除 SYSTEM 排程任務與刪除連線設定都需要管理員權限。'
    Write-Host '請改以「以系統管理員身分執行」開啟 PowerShell 再跑一次。'
    Write-Host ''
    Write-Host '（只想先看看會動到什麼的話，加上 -DryRun 不需要管理員權限。）' -ForegroundColor DarkGray
    exit 2
}
Write-Ok "執行身分 — $($identity.Name)$(if (-not $isAdmin) { '（非管理員，但 -DryRun 不會實際變更）' })"

if ($DryRun) {
    Write-Host ''
    Write-Warn '這是預演模式（-DryRun），以下都只是「會做什麼」，不會實際刪除任何東西。'
}

if ($RemoveProfile) {
    if (-not $ProfileName) {
        Write-Bad '指定了 -RemoveProfile，但沒有給 -ProfileName，不知道要刪哪一筆。'
        exit 2
    }
    if (-not $CliPath) {
        $candidate = Join-Path $PSScriptRoot '..\dist\cli\pgbackup.exe'
        if (Test-Path -LiteralPath $candidate) {
            $CliPath = (Resolve-Path -LiteralPath $candidate).Path
        }
        else {
            $cmd = Get-Command pgbackup.exe -ErrorAction SilentlyContinue
            if ($cmd) { $CliPath = $cmd.Source }
        }
    }
    if (-not $CliPath -or -not (Test-Path -LiteralPath $CliPath)) {
        Write-Bad '找不到 pgbackup.exe，無法移除連線設定。請以 -CliPath 指定完整路徑。'
        exit 2
    }
    $CliPath = (Resolve-Path -LiteralPath $CliPath).Path
}

if ($RemoveBackups -and -not $BackupDir) {
    Write-Bad '指定了 -RemoveBackups，但沒有給 -BackupDir，不知道要刪哪個目錄。'
    exit 2
}

$storeDir = Join-Path $env:ProgramData 'PostgresBackup'

# ------------------------------------------------------------------ 盤點

Write-Section '盤點：目前有什麼'

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) {
    Write-Info "排程任務    ：$TaskName（狀態 $($task.State)，執行身分 $($task.Principal.UserId)）"
}
else {
    Write-Info "排程任務    ：找不到「$TaskName」"
}

if (Test-Path -LiteralPath $storeDir) {
    $storeFiles = @(Get-ChildItem -LiteralPath $storeDir -File -ErrorAction SilentlyContinue)
    Write-Info "連線設定存放區：$storeDir（$($storeFiles.Count) 個檔案）"
}
else {
    Write-Info "連線設定存放區：不存在"
}

# 大小用人看得懂的單位，不要讓 200 MB 顯示成 0.00 GB
function Format-Size([double]$bytes) {
    if ($bytes -ge 1GB) { return '{0:N2} GB' -f ($bytes / 1GB) }
    if ($bytes -ge 1MB) { return '{0:N1} MB' -f ($bytes / 1MB) }
    return '{0:N0} KB' -f ($bytes / 1KB)
}

$backupStats = $null
if ($BackupDir -and (Test-Path -LiteralPath $BackupDir)) {
    $allFiles = @(Get-ChildItem -LiteralPath $BackupDir -Recurse -File -ErrorAction SilentlyContinue)

    # 備份檔只算第一層；snapshots\ 裡的是還原前快照，性質不同，分開列。
    $dumps = @(Get-ChildItem -LiteralPath $BackupDir -File -ErrorAction SilentlyContinue |
               Where-Object { $_.Extension -in @('.dump', '.sql', '.tar') })
    $snapDir = Join-Path $BackupDir 'snapshots'
    $snaps = @()
    if (Test-Path -LiteralPath $snapDir) {
        $snaps = @(Get-ChildItem -LiteralPath $snapDir -Recurse -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.Extension -in @('.dump', '.sql', '.tar') })
    }

    $backupStats = [pscustomobject]@{
        Count     = $dumps.Count
        Bytes     = [double](($dumps | Measure-Object Length -Sum).Sum)
        Newest    = ($dumps | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
        SnapCount = $snaps.Count
        SnapBytes = [double](($snaps | Measure-Object Length -Sum).Sum)
        AllCount  = $allFiles.Count
        AllBytes  = [double](($allFiles | Measure-Object Length -Sum).Sum)
    }

    Write-Info ("備份目錄    ：$BackupDir")
    Write-Info ("              備份檔 {0} 個，{1}" -f $dumps.Count, (Format-Size $backupStats.Bytes))
    if ($backupStats.Newest) {
        Write-Info ("              最新：{0}（{1:yyyy-MM-dd HH:mm}）" -f $backupStats.Newest.Name, $backupStats.Newest.LastWriteTime)
    }
    if ($snaps.Count -gt 0) {
        Write-Info ("              還原前快照 {0} 個，{1}" -f $snaps.Count, (Format-Size $backupStats.SnapBytes))
    }
}
elseif ($BackupDir) {
    Write-Info "備份目錄    ：$BackupDir（不存在）"
}

# ------------------------------------------------------------------ 1. 排程任務

Write-Section '1. 排程任務'

if (-not $task) {
    Write-Skip "找不到排程任務「$TaskName」，不需要移除。"
    Write-Info '若你當初用了不同的任務名稱，請以 -TaskName 指定。'
    Write-Info '列出所有相關任務：Get-ScheduledTask | Where-Object TaskName -like "*PostgresBackup*"'
}
elseif ($DryRun) {
    Write-Plan "會移除排程任務「$TaskName」"
}
else {
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Ok "已移除排程任務「$TaskName」"
        $script:Removed += "排程任務 $TaskName"
    }
    catch {
        Write-Bad "移除排程任務失敗：$($_.Exception.Message)"
    }
}

# ------------------------------------------------------------------ 2. 連線設定

Write-Section '2. 連線設定'

if (-not $RemoveProfile) {
    Write-Skip '未指定 -RemoveProfile，保留連線設定。'
    Write-Info '連線設定留著不會造成任何問題——它只是一筆加密保存的連線資訊，沒有排程來用它。'
    Write-Info '日後要刪：pgbackup profile remove --name "<名稱>"（需管理員身分）'
    $script:Kept += '連線設定'
}
elseif ($DryRun) {
    Write-Plan "會移除連線設定「$ProfileName」（含其加密密碼）"
}
else {
    try {
        $output = & $CliPath profile remove --name $ProfileName 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "已移除連線設定「$ProfileName」（其加密密碼一併清除）"
            $script:Removed += "連線設定 $ProfileName"
        }
        else {
            Write-Bad "移除連線設定失敗（退出碼 $LASTEXITCODE）"
            $output | ForEach-Object { Write-Info "  $_" }
        }
    }
    catch {
        Write-Bad "移除連線設定失敗：$($_.Exception.Message)"
    }
}

# ------------------------------------------------------------------ 3. 存放區

Write-Section '3. 連線設定存放區'

if (-not $RemoveStore) {
    Write-Skip '未指定 -RemoveStore，保留存放區目錄。'
    if (Test-Path -LiteralPath $storeDir) {
        Write-Info "$storeDir 會留著（權限仍限定於系統管理員與 SYSTEM）。"
        Write-Info '留著是安全的：裡面的密碼是加密的，且目錄權限本來就擋住一般使用者。'
        Write-Info '同一台機器上若還有別的排程或手動作業在用命令列連線設定，刪掉會一起弄壞。'
    }
    $script:Kept += '存放區目錄'
}
elseif (-not (Test-Path -LiteralPath $storeDir)) {
    Write-Skip "存放區目錄不存在：$storeDir"
}
else {
    # 裡面還有沒有別的連線設定？有的話別誤刪——同一台機器可能有多組排程共用這個存放區。
    $remaining = $null
    if ($CliPath -and (Test-Path -LiteralPath $CliPath)) {
        $listOut = [string](& $CliPath profile list 2>&1)
        $remaining = ($listOut -notmatch '目前沒有任何命令列連線設定')
    }

    if ($DryRun) {
        if ($remaining -and -not $Force) {
            Write-Plan "不會刪除存放區：裡面仍有其他連線設定（要強制刪除需加 -Force）"
        }
        else {
            Write-Plan "會刪除整個存放區目錄：$storeDir"
        }
    }
    elseif ($remaining -and -not $Force) {
        Write-Bad '存放區中仍有其他連線設定，因此不刪除。'
        Write-Info '同一台機器上可能有別的排程共用這個存放區，刪掉會一起弄壞。'
        Write-Info '確定要刪請加上 -Force，或先用 pgbackup profile list 確認內容。'
    }
    else {
        $ok = Confirm-Destructive `
                "即將刪除連線設定存放區 $storeDir（所有命令列連線設定與其加密密碼都會消失）。" `
                'DELETE'
        if (-not $ok) {
            Write-Skip '已取消，存放區保留。'
        }
        else {
            try {
                Remove-Item -LiteralPath $storeDir -Recurse -Force
                Write-Ok "已刪除存放區目錄 $storeDir"
                $script:Removed += '存放區目錄'
            }
            catch {
                Write-Bad "刪除存放區失敗：$($_.Exception.Message)"
            }
        }
    }
}

# ------------------------------------------------------------------ 4. 備份檔

Write-Section '4. 備份檔'

if (-not $RemoveBackups) {
    Write-Skip '未指定 -RemoveBackups，保留所有備份檔。'
    if ($backupStats) {
        Write-Info ("$BackupDir 裡的 {0} 個備份檔（{1}）原封不動。" -f $backupStats.Count, (Format-Size $backupStats.Bytes))
    }
    Write-Info '這是預設行為：停掉排程跟丟掉既有備份是兩件不同的事。'
    $script:Kept += '備份檔'
}
elseif (-not (Test-Path -LiteralPath $BackupDir)) {
    Write-Skip "備份目錄不存在：$BackupDir"
}
elseif ($DryRun) {
    Write-Plan "會刪除整個備份目錄：$BackupDir"
    if ($backupStats) {
        Write-Plan ("  其中含 {0} 個備份檔、{1} 個還原前快照，共 {2} 個檔案 {3}（含日誌）" -f
            $backupStats.Count, $backupStats.SnapCount, $backupStats.AllCount, (Format-Size $backupStats.AllBytes))
    }
}
else {
    Write-Host ''
    Write-Warn '接下來要刪除的是「資料」，不是設定。刪掉之後拿不回來。'
    if ($backupStats) {
        Write-Info ("將刪除 {0} 個備份檔與 {1} 個還原前快照，合計 {2}" -f $backupStats.Count, $backupStats.SnapCount, (Format-Size $backupStats.AllBytes))
        if ($backupStats.Newest) {
            Write-Info ("最新的一份是 {0:yyyy-MM-dd HH:mm} 的 {1}" -f $backupStats.Newest.LastWriteTime, $backupStats.Newest.Name)
        }
    }

    # 比照本工具還原作業的防呆：要求輸入目錄完整路徑，不是打個 y 就算數。
    $ok = Confirm-Destructive `
            "即將刪除整個備份目錄 $BackupDir，包含所有備份檔、還原前快照與日誌。" `
            $BackupDir
    if (-not $ok) {
        Write-Skip '已取消，備份檔保留。'
        $script:Kept += '備份檔'
    }
    else {
        try {
            Remove-Item -LiteralPath $BackupDir -Recurse -Force
            Write-Ok "已刪除備份目錄 $BackupDir"
            $script:Removed += '備份目錄'
        }
        catch {
            Write-Bad "刪除備份目錄失敗：$($_.Exception.Message)"
        }
    }
}

# ------------------------------------------------------------------ 結論

Write-Section '結論'

if ($DryRun) {
    Write-Host ''
    Write-Host '  以上為預演，沒有任何東西被刪除。' -ForegroundColor Magenta
    Write-Host '  確認無誤後，把 -DryRun 拿掉重跑一次即可。' -ForegroundColor Magenta
    Write-Host ''
    exit 0
}

Write-Host ''
if ($script:Removed.Count -gt 0) {
    Write-Host '  已移除：' -ForegroundColor White
    $script:Removed | ForEach-Object { Write-Host "    - $_" -ForegroundColor Green }
}
if ($script:Kept.Count -gt 0) {
    Write-Host '  保留：' -ForegroundColor White
    $script:Kept | ForEach-Object { Write-Host "    - $_" -ForegroundColor DarkGray }
}

# 稽核歷史不會被上面任何一步刪到，明講出來，免得有人以為都清乾淨了。
Write-Host ''
Write-Host '  以下不會被本腳本移除：' -ForegroundColor DarkGray
Write-Host "    - 排程執行的稽核歷史：$(Join-Path $env:SystemRoot 'System32\config\systemprofile\AppData\Local\PostgresBackup\history.db')" -ForegroundColor DarkGray
Write-Host "    - 你手動操作的稽核歷史：$(Join-Path $env:LOCALAPPDATA 'PostgresBackup\history.db')" -ForegroundColor DarkGray
Write-Host '    - pgbackup.exe 與 PostgreSQL 客戶端工具本身' -ForegroundColor DarkGray

if ($script:Failures.Count -gt 0) {
    Write-Host ''
    Write-Host "  $($script:Failures.Count) 項失敗：" -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
    Write-Host ''
    exit 1
}

Write-Host ''
Write-Host '  全部順利完成。' -ForegroundColor Green
Write-Host ''
exit 0
