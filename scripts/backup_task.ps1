<#
================================================================================
 backup_task.ps1 — PostgresBackup 排程備份腳本

 這支腳本是「排程任務每天實際執行的東西」。它做四件事：

   1. 呼叫 pgbackup 備份一次資料庫
   2. 用退出碼判斷成敗（不是用「目錄裡有沒有檔案」判斷）
   3. 失敗時把這一輪留下的 0 位元組空檔清掉
   4. 依保留天數清理過期的備份檔、還原前快照與日誌

 本檔案不含任何資料庫密碼，可以安全地放在共用位置，也可以簽入版控。
 密碼由 `pgbackup profile set` 事先以機器範圍加密存放，這裡只用 --profile 參照。

 第一次設定請改用同目錄的 register-backup-task.ps1，它會引導你建立連線設定、
 註冊排程並立即試跑一次。這支腳本是它註冊進工作排程器的目標。

 手動測試（以系統管理員身分執行，因為要讀機器範圍的連線設定）：
   .\backup_task.ps1 -ProfileName "正式環境" -BackupDir "D:\DatabaseBackups\my_database" `
                     -PgBinPath "C:\Tools\pgsql\bin" -CliPath "C:\Tools\PostgresBackup\pgbackup.exe"

 退出碼：0 = 成功 / 1 = 備份失敗 / 2 = 設定或環境有問題（還沒開始備份就停了）
================================================================================
#>
[CmdletBinding()]
param(
    # --- 必要 ---------------------------------------------------------------
    [Parameter(Mandatory = $true)]
    [string]$ProfileName,                       # 命令列連線設定名稱（pgbackup profile set 建立的那個）

    [Parameter(Mandatory = $true)]
    [string]$BackupDir,                         # 備份檔輸出目錄

    [Parameter(Mandatory = $true)]
    [string]$CliPath,                           # pgbackup.exe 的完整路徑

    # --- 客戶端工具 ----------------------------------------------------------
    # 官方工具已加入系統 PATH（手冊 §2 方法 B）時可以留空；
    # 用免安裝可攜版（方法 A）時必填，否則會得到「未偵測到 PostgreSQL 客戶端工具」。
    [string]$PgBinPath,

    # --- 備份選項 ------------------------------------------------------------
    [ValidateSet('custom', 'plain')]
    [string]$Format = 'custom',                 # custom = 自訂二進位（建議）；plain = 純文字 SQL

    [ValidateSet('all', 'schema', 'data')]
    [string]$Mode = 'all',

    # --- 保留政策 ------------------------------------------------------------
    # 三者分開設定：快照是「還原前的救命繩」，性質跟例行備份不同，通常想留久一點。
    [int]$RetentionDays = 7,                    # 例行備份檔保留天數
    [int]$SnapshotRetentionDays = 30,           # snapshots\ 裡的還原前快照保留天數
    [int]$LogRetentionDays = 30,                # logs\ 裡的執行日誌保留天數

    [switch]$SkipCleanup                        # 只備份，完全不執行任何清理
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------ 日誌

$LogDir = Join-Path $BackupDir 'logs'
$LogFile = $null

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')

    $line = "[{0}] [{1}] {2}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Level, $Message

    # 排程以 SYSTEM 身分執行時沒有主控台，但手動執行時看得到，所以兩邊都寫。
    Write-Output $line

    if ($LogFile) {
        try { Add-Content -LiteralPath $LogFile -Value $line -Encoding utf8 } catch { }
    }
}

function Stop-WithError {
    param([string]$Message, [int]$Code)
    Write-Log $Message 'ERROR'
    Write-Log "=== 排程作業結束（退出碼 $Code）===" 'ERROR'
    exit $Code
}

# ------------------------------------------------------------------ 前置檢查

# 目錄要先建起來，日誌才有地方寫。
foreach ($dir in @($BackupDir, $LogDir)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

$LogFile = Join-Path $LogDir ("backup_{0}.log" -f (Get-Date).ToString('yyyyMMdd_HHmmss'))

Write-Log "=== 開始執行 PostgreSQL 排程備份 ==="
Write-Log "連線設定：$ProfileName"
Write-Log "輸出目錄：$BackupDir"

if (-not (Test-Path -LiteralPath $CliPath)) {
    Stop-WithError "找不到 pgbackup.exe：$CliPath。請確認 -CliPath 正確，且該位置是 SYSTEM 讀得到的（不要放在個人資料夾底下）。" 2
}

if ($PgBinPath -and -not (Test-Path -LiteralPath $PgBinPath)) {
    Stop-WithError "找不到客戶端工具目錄：$PgBinPath。請確認 -PgBinPath 正確。" 2
}

# ------------------------------------------------------------------ 備份

# 先記下備份前就已存在的檔案。備份失敗時，pg_dump 會留下一個 0 位元組的空檔
# （它先建檔再連資料庫），靠這份清單才知道哪個是「這一輪新產生的」而不會誤刪別人的東西。
$filesBefore = @{}
foreach ($f in @(Get-ChildItem -LiteralPath $BackupDir -File -ErrorAction SilentlyContinue)) {
    $filesBefore[$f.FullName] = $true
}

$cliArgs = @(
    'backup'
    '--profile', $ProfileName
    '-f', $Format
    '-m', $Mode
    '-o', $BackupDir
    '--log', $LogFile
)
if ($PgBinPath) { $cliArgs += @('--pg-bin-path', $PgBinPath) }

Write-Log "呼叫：$CliPath $($cliArgs -join ' ')"

& $CliPath @cliArgs
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Log "備份失敗，pgbackup 回傳退出碼 $exitCode。詳細錯誤見上方 pg_dump 的輸出。" 'ERROR'

    # 清掉這一輪留下的空檔，不要讓它堆在備份目錄裡冒充備份。
    $orphans = @(Get-ChildItem -LiteralPath $BackupDir -File -ErrorAction SilentlyContinue |
                 Where-Object { -not $filesBefore.ContainsKey($_.FullName) -and $_.Length -eq 0 })
    foreach ($o in $orphans) {
        try {
            Remove-Item -LiteralPath $o.FullName -Force
            Write-Log "已清除本次失敗留下的 0 位元組空檔：$($o.Name)" 'WARN'
        }
        catch {
            Write-Log "無法清除空檔 $($o.Name)：$($_.Exception.Message)" 'WARN'
        }
    }

    Write-Log "=== 排程作業結束（退出碼 1）===" 'ERROR'
    exit 1
}

# 成功。把這一輪產出的檔案記進日誌，方便事後對帳。
$produced = @(Get-ChildItem -LiteralPath $BackupDir -File -ErrorAction SilentlyContinue |
              Where-Object { -not $filesBefore.ContainsKey($_.FullName) })
foreach ($p in $produced) {
    Write-Log ("備份完成：{0}（{1:N2} MB）" -f $p.Name, ($p.Length / 1MB))
}

# ------------------------------------------------------------------ 保留政策

# 清理失敗不該讓整個作業被判定失敗——備份本身已經成功了。
# 所以這一段的錯誤一律降級為 WARN，不影響退出碼。
function Remove-Expired {
    param([string]$Path, [int]$Days, [string]$Label, [switch]$Recurse)

    if ($Days -le 0) {
        Write-Log "$Label：保留天數設為 $Days，略過清理。"
        return
    }
    if (-not (Test-Path -LiteralPath $Path)) { return }

    $cutoff = (Get-Date).AddDays(-$Days)

    # 注意：這裡刻意不用 Get-ChildItem -Include。
    # -Include 在沒有 -Recurse、且路徑結尾沒有 \* 的情況下會被完全忽略，
    # 結果是一個檔案都不刪卻毫無錯誤訊息——備份目錄會無聲無息地一直長大。
    # 改用取回全部檔案再以副檔名過濾，行為明確不會踩到那個坑。
    $gciArgs = @{ LiteralPath = $Path; File = $true; ErrorAction = 'SilentlyContinue' }
    if ($Recurse) { $gciArgs['Recurse'] = $true }

    $expired = @(Get-ChildItem @gciArgs |
                 Where-Object { $_.Extension -in @('.dump', '.sql', '.tar') -and $_.LastWriteTime -lt $cutoff })

    if ($expired.Count -eq 0) {
        Write-Log "$Label：沒有超過 $Days 天的檔案需要清理。"
        return
    }

    $freed = 0
    $removed = 0
    foreach ($file in $expired) {
        try {
            $size = $file.Length
            Remove-Item -LiteralPath $file.FullName -Force
            $freed += $size
            $removed++
            Write-Log ("$Label：已清除 {0}（{1:yyyy-MM-dd}）" -f $file.Name, $file.LastWriteTime)
        }
        catch {
            Write-Log "$Label：無法清除 $($file.Name)：$($_.Exception.Message)" 'WARN'
        }
    }
    Write-Log ("$Label：共清除 {0} 個檔案，釋出 {1:N2} MB。" -f $removed, ($freed / 1MB))
}

if ($SkipCleanup) {
    Write-Log '已指定 -SkipCleanup，略過所有清理。'
}
else {
    try {
        # 備份檔：只掃第一層，不遞迴——snapshots\ 與 logs\ 由下面兩段各自處理。
        Remove-Expired -Path $BackupDir -Days $RetentionDays -Label '備份檔'

        # 還原前快照：pgbackup restore 會把快照寫到「來源備份檔所在目錄」下的 snapshots\。
        Remove-Expired -Path (Join-Path $BackupDir 'snapshots') -Days $SnapshotRetentionDays -Label '還原前快照' -Recurse

        # 日誌：副檔名不同，單獨處理。
        if ($LogRetentionDays -gt 0) {
            $logCutoff = (Get-Date).AddDays(-$LogRetentionDays)
            $oldLogs = @(Get-ChildItem -LiteralPath $LogDir -File -ErrorAction SilentlyContinue |
                         Where-Object { $_.Extension -eq '.log' -and $_.LastWriteTime -lt $logCutoff -and $_.FullName -ne $LogFile })
            foreach ($l in $oldLogs) {
                try { Remove-Item -LiteralPath $l.FullName -Force } catch { }
            }
            Write-Log "日誌：已清除 $($oldLogs.Count) 個超過 $LogRetentionDays 天的日誌檔。"
        }
    }
    catch {
        Write-Log "清理過程發生非預期錯誤：$($_.Exception.Message)（備份本身已成功，不影響退出碼）" 'WARN'
    }
}

Write-Log '=== 排程作業順利結束（退出碼 0）==='
exit 0
