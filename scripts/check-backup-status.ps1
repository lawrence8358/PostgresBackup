<#
================================================================================
 check-backup-status.ps1 — 查看排程備份跑得好不好

 **不需要系統管理員權限。** 一般身分就能跑，這是刻意的：

   檢查備份結果不該需要提權。需要提權的只有「讀取連線設定存放區」那一件事
   （也就是 pgbackup profile list，看的是主機／帳號／密碼有沒有設），
   跟備份跑得好不好完全無關。

 它會告訴你四件事：

   1. 排程任務還在不在、下次什麼時候跑、上次跑的結果是什麼
   2. 最近產出了哪些備份檔、多大、多久以前
   3. 最新一次執行日誌的結尾
   4. 備份目錄所在磁碟還剩多少空間

 用法：
   .\check-backup-status.ps1 -BackupDir "D:\DatabaseBackups\my_database"
   .\check-backup-status.ps1 -BackupDir "D:\..." -TaskName "PostgresBackup_Daily" -WarnAfterHours 26

 退出碼：0 = 一切正常 / 1 = 有需要注意的事（適合接到監控系統）
================================================================================
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupDir,

    [string]$TaskName = 'PostgresBackup_Daily',

    # 最新的備份超過這個時數就視為異常。預設 26 小時：每日排程 + 2 小時緩衝。
    [int]$WarnAfterHours = 26,

    # 日誌結尾要印幾行
    [int]$LogTailLines = 15
)

$ErrorActionPreference = 'Continue'
$script:Problems = @()

function Write-Section($text) {
    Write-Host ''
    Write-Host "== $text " -ForegroundColor Cyan -NoNewline
    Write-Host ('=' * [Math]::Max(0, 68 - $text.Length)) -ForegroundColor Cyan
}
function Write-Ok($t)   { Write-Host '[正常] ' -ForegroundColor Green  -NoNewline; Write-Host $t }
function Write-Warn($t) { Write-Host '[注意] ' -ForegroundColor Yellow -NoNewline; Write-Host $t; $script:Problems += $t }
function Write-Bad($t)  { Write-Host '[異常] ' -ForegroundColor Red    -NoNewline; Write-Host $t; $script:Problems += $t }
function Write-Info($t) { Write-Host "       $t" -ForegroundColor DarkGray }

# 把工作排程器的退出碼翻成人話
function Get-ResultMeaning([int64]$code) {
    switch ($code) {
        0          { '成功' }
        1          { '備份失敗（pgbackup 回報錯誤，請看下方日誌）' }
        2          { '設定或環境有問題，備份根本沒開始（路徑錯誤、找不到客戶端工具…）' }
        267009     { '目前仍在執行中' }          # 0x41301
        267011     { '從來沒有執行過' }          # 0x41303
        267014     { '上一次執行被中止' }        # 0x41306
        2147942401 { '找不到要執行的程式（檢查腳本路徑）' }
        default    { "未預期的退出碼（0x$($code.ToString('X')))" }
    }
}

Write-Host ''
Write-Host 'PostgresBackup — 排程備份狀態檢查' -ForegroundColor White
Write-Host ("執行身分：{0}（{1}）" -f
    [Security.Principal.WindowsIdentity]::GetCurrent().Name,
    $(if ((New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) { '系統管理員' } else { '一般身分，足夠了' })
) -ForegroundColor DarkGray

# ------------------------------------------------------------------ 1. 排程任務

Write-Section '1. 排程任務'

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $task) {
    Write-Bad "找不到排程任務「$TaskName」。"
    Write-Info '可能還沒註冊，或名稱不同。註冊請跑同目錄的 register-backup-task.ps1。'
}
else {
    Write-Ok "任務存在 — 狀態：$($task.State)，執行身分：$($task.Principal.UserId)"

    if ($task.State -eq 'Disabled') {
        Write-Bad '任務目前是「已停用」狀態，不會自動執行。'
        Write-Info "重新啟用：Enable-ScheduledTask -TaskName `"$TaskName`""
    }

    $info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($info) {
        $code = [int64]$info.LastTaskResult
        $meaning = Get-ResultMeaning $code

        Write-Info "上次執行：$($info.LastRunTime)"
        Write-Info "下次執行：$($info.NextRunTime)"

        if ($code -eq 0) {
            Write-Ok "上次執行結果：$code（$meaning）"
        }
        elseif ($code -eq 267009) {
            Write-Info "上次執行結果：$code（$meaning）"
        }
        else {
            Write-Bad "上次執行結果：$code（$meaning）"
        }
    }
}

# ------------------------------------------------------------------ 2. 備份檔

Write-Section '2. 備份檔'

if (-not (Test-Path -LiteralPath $BackupDir)) {
    Write-Bad "備份目錄不存在：$BackupDir"
}
else {
    Write-Info "目錄：$BackupDir"

    $backups = @(Get-ChildItem -LiteralPath $BackupDir -File -ErrorAction SilentlyContinue |
                 Where-Object { $_.Extension -in @('.dump', '.sql', '.tar') } |
                 Sort-Object LastWriteTime -Descending)

    if ($backups.Count -eq 0) {
        Write-Bad '備份目錄裡一個備份檔都沒有。'
    }
    else {
        $newest = $backups[0]
        $ageHours = [Math]::Round(((Get-Date) - $newest.LastWriteTime).TotalHours, 1)

        if ($newest.Length -eq 0) {
            Write-Bad "最新的備份檔是 0 位元組：$($newest.Name)"
            Write-Info '這通常表示備份失敗（pg_dump 先建檔才連資料庫）。請看下方日誌。'
        }
        elseif ($ageHours -gt $WarnAfterHours) {
            Write-Bad ("最新備份是 {0} 小時前的（超過 {1} 小時門檻）：{2}" -f $ageHours, $WarnAfterHours, $newest.Name)
            Write-Info '排程可能已經停掉一段時間了。'
        }
        else {
            Write-Ok ("最新備份：{0}（{1:N2} MB，{2} 小時前）" -f $newest.Name, ($newest.Length / 1MB), $ageHours)
        }

        $zeroByte = @($backups | Where-Object { $_.Length -eq 0 })
        if ($zeroByte.Count -gt 0) {
            Write-Warn "目錄中有 $($zeroByte.Count) 個 0 位元組的空檔，是失敗留下的殘骸。"
            $zeroByte | Select-Object -First 5 | ForEach-Object { Write-Info "  $($_.Name)" }
        }

        Write-Host ''
        Write-Info "最近 5 次備份："
        $backups | Select-Object -First 5 | ForEach-Object {
            Write-Info ("  {0:yyyy-MM-dd HH:mm}  {1,10:N2} MB  {2}" -f $_.LastWriteTime, ($_.Length / 1MB), $_.Name)
        }
        Write-Info ("共 {0} 個備份檔，合計 {1:N2} GB" -f $backups.Count, (($backups | Measure-Object Length -Sum).Sum / 1GB))
    }

    # 還原前快照
    $snapDir = Join-Path $BackupDir 'snapshots'
    if (Test-Path -LiteralPath $snapDir) {
        $snaps = @(Get-ChildItem -LiteralPath $snapDir -Recurse -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.Extension -in @('.dump', '.sql') })
        if ($snaps.Count -gt 0) {
            Write-Host ''
            Write-Info ("還原前快照：{0} 個，合計 {1:N2} GB（{2}）" -f
                $snaps.Count, (($snaps | Measure-Object Length -Sum).Sum / 1GB), $snapDir)
        }
    }
}

# ------------------------------------------------------------------ 3. 執行日誌

Write-Section '3. 最新執行日誌'

$logDir = Join-Path $BackupDir 'logs'
if (-not (Test-Path -LiteralPath $logDir)) {
    Write-Warn "找不到日誌目錄：$logDir"
}
else {
    $latestLog = Get-ChildItem -LiteralPath $logDir -Filter '*.log' -File -ErrorAction SilentlyContinue |
                 Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latestLog) {
        Write-Warn '日誌目錄裡沒有任何日誌檔。'
    }
    else {
        Write-Info "$($latestLog.Name)（$($latestLog.LastWriteTime)）"
        Write-Host ''
        try {
            Get-Content -LiteralPath $latestLog.FullName -Tail $LogTailLines -ErrorAction Stop |
                ForEach-Object {
                    $color = if ($_ -match '\[ERROR\]|失敗') { 'Red' }
                             elseif ($_ -match '\[WARN\]|警告') { 'Yellow' }
                             elseif ($_ -match 'SUCCESS|順利') { 'Green' }
                             else { 'DarkGray' }
                    Write-Host "    $_" -ForegroundColor $color
                }
        }
        catch {
            Write-Bad "讀不到日誌檔：$($_.Exception.Message)"
            Write-Info '若是權限問題，請確認備份目錄允許你讀取（備份目錄不該被鎖成只有 SYSTEM 能讀）。'
        }
    }
}

# ------------------------------------------------------------------ 4. 磁碟空間

Write-Section '4. 磁碟空間'

try {
    $root = [IO.Path]::GetPathRoot((Resolve-Path -LiteralPath $BackupDir -ErrorAction Stop).Path)
    $drive = Get-PSDrive -Name $root.TrimEnd('\', ':') -ErrorAction Stop
    $freeGB = $drive.Free / 1GB
    $totalGB = ($drive.Free + $drive.Used) / 1GB
    $pct = if ($totalGB -gt 0) { $freeGB / $totalGB * 100 } else { 0 }

    if ($pct -lt 10) {
        Write-Bad ("{0} 剩餘 {1:N1} GB / {2:N1} GB（{3:N0}%）—— 空間不足，備份可能會失敗。" -f $root, $freeGB, $totalGB, $pct)
    }
    elseif ($pct -lt 20) {
        Write-Warn ("{0} 剩餘 {1:N1} GB / {2:N1} GB（{3:N0}%）" -f $root, $freeGB, $totalGB, $pct)
    }
    else {
        Write-Ok ("{0} 剩餘 {1:N1} GB / {2:N1} GB（{3:N0}%）" -f $root, $freeGB, $totalGB, $pct)
    }
    Write-Info '提醒：還原作業會在 snapshots\ 再寫一份完整備份，請預留空間。'
}
catch {
    Write-Warn "無法取得磁碟空間資訊：$($_.Exception.Message)"
}

# ------------------------------------------------------------------ 結論

Write-Section '結論'

if ($script:Problems.Count -eq 0) {
    Write-Host ''
    Write-Host '  一切正常。' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  $($script:Problems.Count) 項需要注意：" -ForegroundColor Yellow
$script:Problems | ForEach-Object { Write-Host "    - $_" -ForegroundColor Yellow }
Write-Host ''
Write-Host '  要看連線設定本身（主機／帳號／密碼狀態），需要系統管理員身分：' -ForegroundColor DarkGray
Write-Host '    pgbackup profile list' -ForegroundColor DarkGray
Write-Host ''
exit 1
