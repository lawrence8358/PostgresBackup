<#
================================================================================
 register-backup-task.ps1 — PostgresBackup 排程備份一次性設定

 從零到排程就緒，跑這一支就好。它依序做四件事：

   1. 以 `pgbackup profile set` 建立命令列連線設定（密碼遮蔽輸入，存檔前實際連線驗證）
   2. 確認設定確實寫進機器範圍存放區，且存放區權限正常
   3. 以 SYSTEM 身分把 backup_task.ps1 註冊進 Windows 工作排程器
   4. 立刻試跑一次，確認整條路真的通

 必須以「系統管理員」身分開啟的 PowerShell 執行——建立連線設定要寫入
 %ProgramData%，註冊 SYSTEM 排程也需要管理員權限。

 密碼一律以互動式遮蔽輸入取得，不接受以參數傳入，也不會寫進任何檔案或排程指令。

 用法：
   .\register-backup-task.ps1
   .\register-backup-task.ps1 -Database my_database -Username postgres -At 02:00
   .\register-backup-task.ps1 -SkipProfile          # 連線設定已經建好了，只註冊排程

 移除：
   Unregister-ScheduledTask -TaskName "PostgresBackup_Daily" -Confirm:$false
   pgbackup profile remove --name "<你的設定名稱>"
================================================================================
#>
[CmdletBinding()]
param(
    # --- 連線 ---------------------------------------------------------------
    [string]$ProfileName,
    [string]$ServerHost = 'localhost',
    [int]$Port = 5432,
    [string]$Database,
    [string]$Username,

    # --- 路徑 ---------------------------------------------------------------
    [string]$CliPath,                           # pgbackup.exe
    [string]$PgBinPath,                         # 官方客戶端工具的 bin 目錄（已在 PATH 則可留空）
    [string]$BackupDir,                         # 備份輸出目錄
    [string]$ScriptPath,                        # backup_task.ps1（預設為本腳本同目錄）

    # --- 排程 ---------------------------------------------------------------
    [string]$TaskName = 'PostgresBackup_Daily',
    [string]$At = '02:00',

    # --- 保留政策 ------------------------------------------------------------
    [int]$RetentionDays = 7,
    [int]$SnapshotRetentionDays = 30,

    [switch]$SkipProfile,                       # 連線設定已建好，只註冊排程
    [switch]$SkipTestRun                        # 註冊完不要立刻試跑
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------ 輸出小工具

function Write-Section($text) {
    Write-Host ''
    Write-Host "== $text " -ForegroundColor Cyan -NoNewline
    Write-Host ('=' * [Math]::Max(0, 68 - $text.Length)) -ForegroundColor Cyan
}
function Write-Ok($text)   { Write-Host '[OK]   ' -ForegroundColor Green  -NoNewline; Write-Host $text }
function Write-Warn($text) { Write-Host '[注意] ' -ForegroundColor Yellow -NoNewline; Write-Host $text }
function Write-Bad($text)  { Write-Host '[錯誤] ' -ForegroundColor Red    -NoNewline; Write-Host $text }
function Write-Info($text) { Write-Host "       $text" -ForegroundColor DarkGray }

function Read-Required($prompt, $current, $default) {
    if ($current) { return $current }
    while ($true) {
        $hint = if ($default) { " [$default]" } else { '' }
        $value = Read-Host "$prompt$hint"
        if (-not $value -and $default) { return $default }
        if ($value) { return $value }
        Write-Bad '這一項必填。'
    }
}

# ------------------------------------------------------------------ 前置檢查

Write-Section '前置檢查'

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Bad "目前不是系統管理員身分（$($identity.Name)）。"
    Write-Host ''
    Write-Host '建立命令列連線設定要寫入 %ProgramData%\PostgresBackup，註冊 SYSTEM 排程也需要'
    Write-Host '管理員權限。請關掉這個視窗，改以「以系統管理員身分執行」開啟 PowerShell 再跑一次。'
    exit 1
}
Write-Ok "以系統管理員身分執行 — $($identity.Name)"

# backup_task.ps1
if (-not $ScriptPath) { $ScriptPath = Join-Path $PSScriptRoot 'backup_task.ps1' }
if (-not (Test-Path -LiteralPath $ScriptPath)) {
    Write-Bad "找不到 backup_task.ps1：$ScriptPath"
    Write-Info '請以 -ScriptPath 指定它的完整路徑。'
    exit 1
}
$ScriptPath = (Resolve-Path -LiteralPath $ScriptPath).Path
Write-Ok "備份腳本 — $ScriptPath"

# pgbackup.exe
if (-not $CliPath) {
    # 先看專案結構裡的相對位置，再看系統 PATH。都找不到就問——
    # 刻意不猜任何固定的安裝路徑，那只會在別人的機器上猜錯。
    $candidate = Join-Path $PSScriptRoot '..\dist\cli\pgbackup.exe'
    if (Test-Path -LiteralPath $candidate) {
        $CliPath = (Resolve-Path -LiteralPath $candidate).Path
    }
    else {
        $cmd = Get-Command pgbackup.exe -ErrorAction SilentlyContinue
        if ($cmd) { $CliPath = $cmd.Source }
    }
}
$CliPath = Read-Required 'pgbackup.exe 的完整路徑' $CliPath $null
if (-not (Test-Path -LiteralPath $CliPath)) {
    Write-Bad "找不到 pgbackup.exe：$CliPath"
    exit 1
}
$CliPath = (Resolve-Path -LiteralPath $CliPath).Path
Write-Ok "pgbackup.exe — $CliPath"

# 排程以 SYSTEM 身分執行，讀不到個人資料夾。
$userProfileRoot = [Environment]::GetFolderPath('UserProfile')
foreach ($pathToCheck in @(@{P=$CliPath; N='pgbackup.exe'}, @{P=$ScriptPath; N='backup_task.ps1'})) {
    if ($pathToCheck.P.StartsWith($userProfileRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warn "$($pathToCheck.N) 放在你的個人資料夾底下（$userProfileRoot）。"
        Write-Info 'SYSTEM 讀不到個人資料夾，排程會失敗。建議搬到 C:\Tools\ 或某個資料碟的共用目錄。'
    }
}

# 客戶端工具
if ($PgBinPath) {
    if (-not (Test-Path -LiteralPath $PgBinPath)) {
        Write-Bad "找不到客戶端工具目錄：$PgBinPath"
        exit 1
    }
    $PgBinPath = (Resolve-Path -LiteralPath $PgBinPath).Path
}

$checkArgs = @('check-tools')
if ($PgBinPath) { $checkArgs += @('--pg-bin-path', $PgBinPath) }
& $CliPath @checkArgs | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Bad '找不到 PostgreSQL 官方客戶端工具 (pg_dump)。'
    Write-Host ''
    Write-Host '若你用的是免安裝可攜版，請以 -PgBinPath 指定它的 bin 目錄，例如：'
    Write-Host '    -PgBinPath "C:\Tools\pgsql\bin"' -ForegroundColor Yellow
    Write-Host '若尚未安裝，請見使用者手冊第 2 節。'
    Write-Host ''
    Write-Host '提醒：PostgreSQL 跑在 Docker 裡的話，容器內是 Linux 執行檔，Windows 這端用不了，'
    Write-Host '      仍需另外準備 Windows 版的客戶端工具。'
    exit 1
}
Write-Ok "客戶端工具就緒$(if ($PgBinPath) { " — $PgBinPath" } else { '（來自系統 PATH）' })"

# ------------------------------------------------------------------ 連線設定

Write-Section '連線設定'

$ProfileName = Read-Required '連線設定名稱' $ProfileName '正式環境'

if ($SkipProfile) {
    Write-Info "已指定 -SkipProfile，沿用既有的連線設定「$ProfileName」。"
}
else {
    $Database = Read-Required '資料庫名稱' $Database $null
    $Username = Read-Required '資料庫使用者' $Username 'postgres'

    Write-Host ''
    Write-Host "請輸入 $Username@$ServerHost`:$Port/$Database 的資料庫密碼（輸入不會顯示）："
    $secure = Read-Host -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

    if ([string]::IsNullOrEmpty($password)) {
        Write-Bad '未輸入密碼，無法繼續。'
        exit 1
    }

    Write-Host ''
    Write-Info '正在驗證連線並儲存（存檔前會實際連一次資料庫）…'

    # 密碼經標準輸入交給 pgbackup，不會出現在任何命令列上。
    $setOutput = $password | & $CliPath profile set `
        --name $ProfileName -H $ServerHost -P $Port -d $Database -u $Username --password-stdin 2>&1
    $setCode = $LASTEXITCODE

    $password = $null
    [GC]::Collect()

    if ($setCode -ne 0) {
        Write-Bad '連線驗證失敗，連線設定並未儲存。'
        Write-Host ''
        $setOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        Write-Host ''
        Write-Info '常見原因：密碼錯誤、資料庫名稱或使用者打錯、資料庫沒在跑、防火牆擋住。'
        exit 1
    }
    Write-Ok "連線設定「$ProfileName」已通過驗證並儲存"
}

# 確認設定真的讀得到、且存放區權限沒問題
$listOutput = & $CliPath profile list 2>&1
$listText = [string]$listOutput
if ($listText -notmatch [regex]::Escape($ProfileName)) {
    Write-Bad "在 profile list 中找不到「$ProfileName」，無法繼續註冊排程。"
    $listOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    exit 1
}
Write-Ok 'profile list 讀得到這筆設定'

if ($listText -match '\[WARNING\]') {
    Write-Warn '存放區出現權限警告，請看以下輸出：'
    $listOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
}

# ------------------------------------------------------------------ 備份目錄

Write-Section '備份輸出目錄'

# 不給預設值：備份要放哪個磁碟是每台機器都不一樣的決定，猜一個 D:\ 只會在沒有 D 槽的
# 機器上失敗，或更糟——在錯誤的磁碟上默默建出目錄。
$BackupDir = Read-Required '備份檔要放在哪個目錄（例如 D:\DatabaseBackups\my_database）' $BackupDir $null
if (-not (Test-Path -LiteralPath $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    Write-Ok "已建立 $BackupDir"
}
else {
    Write-Ok "$BackupDir"
}
$BackupDir = (Resolve-Path -LiteralPath $BackupDir).Path

if ($BackupDir.StartsWith($userProfileRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Warn '備份目錄位於你的個人資料夾底下，SYSTEM 可能寫不進去。建議改到資料碟。'
}

try {
    $drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot($BackupDir).TrimEnd('\', ':')) -ErrorAction SilentlyContinue
    if ($drive -and $null -ne $drive.Free) {
        Write-Info ("該磁碟剩餘空間：{0:N1} GB" -f ($drive.Free / 1GB))
        Write-Info '提醒：還原前快照會寫到這個目錄底下的 snapshots\，等於要再放得下一份完整備份。'
    }
}
catch { }

# 備份目錄「必須」讓你平常的身分讀得到。
# 排程以 SYSTEM 身分寫入，若這個目錄只有 SYSTEM 有權限，你事後就查不了自己的備份結果——
# 那是不合理的。連線設定存放區需要提權是為了保護密碼；備份檔與日誌沒有這個理由。
try {
    $backupAcl = Get-Acl -LiteralPath $BackupDir
    $readerSids = @(
        $identity.User.Value                                    # 你本人
        'S-1-5-32-545'                                          # BUILTIN\Users
        'S-1-5-11'                                              # Authenticated Users
        'S-1-1-0'                                               # Everyone
    )
    $canRead = $false
    foreach ($rule in $backupAcl.Access) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        try { $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { continue }
        if ($readerSids -contains $sid -and
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Read)) {
            $canRead = $true
            break
        }
    }

    if ($canRead) {
        Write-Ok '你平常的身分讀得到這個目錄（不必提權就能檢查備份結果）'
    }
    else {
        Write-Warn '備份目錄目前似乎沒有開放給你平常的身分讀取。'
        Write-Info '排程以 SYSTEM 身分寫入，你之後會查不到自己的備份結果。建議補上讀取權：'
        Write-Host "         icacls `"$BackupDir`" /grant `"$($identity.Name):(OI)(CI)RX`"" -ForegroundColor Yellow
        Write-Info '（這只影響備份檔與日誌，不影響存放密碼的連線設定存放區。）'
    }
}
catch {
    Write-Info "無法檢查備份目錄權限：$($_.Exception.Message)"
}

# ------------------------------------------------------------------ 註冊排程

Write-Section '註冊排程任務'

# 用 Register-ScheduledTask 而不是 schtasks /TR：後者的命令字串有長度上限（約 261 字元），
# 路徑一長就會被無聲截斷。
$taskArgs = @(
    '-NoProfile'
    '-ExecutionPolicy', 'Bypass'
    '-File', "`"$ScriptPath`""
    '-ProfileName', "`"$ProfileName`""
    '-BackupDir', "`"$BackupDir`""
    '-CliPath', "`"$CliPath`""
    '-RetentionDays', $RetentionDays
    '-SnapshotRetentionDays', $SnapshotRetentionDays
)
if ($PgBinPath) { $taskArgs += @('-PgBinPath', "`"$PgBinPath`"") }

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ($taskArgs -join ' ')
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$sysPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable `
                                         -MultipleInstances IgnoreNew `
                                         -ExecutionTimeLimit (New-TimeSpan -Hours 6) `
                                         -DontStopOnIdleEnd

Register-ScheduledTask -TaskName $TaskName `
                       -Action $action -Trigger $trigger `
                       -Principal $sysPrincipal -Settings $settings `
                       -Description "PostgresBackup 每日排程備份（連線設定：$ProfileName）" `
                       -Force | Out-Null

Write-Ok "已註冊「$TaskName」，每日 $At 以 SYSTEM 身分執行"
Write-Info 'SYSTEM 沒有會到期的密碼，工作排程器也不需要保存任何帳號憑證。'

# ------------------------------------------------------------------ 試跑

if ($SkipTestRun) {
    Write-Info '已指定 -SkipTestRun，略過立即試跑。'
}
else {
    Write-Section '立即試跑一次'

    $before = @(Get-ChildItem -LiteralPath $BackupDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })

    Start-ScheduledTask -TaskName $TaskName
    Write-Info '已觸發，等待完成……'

    # 狀態與退出碼一律向工作排程器的 API 要，不剖析 schtasks 的主控台文字
    # （文字輸出的欄位標籤隨系統語系變化，值何時落盤也不保證）。
    $lastResult = $null
    $waited = 0
    while ($waited -lt 600) {
        Start-Sleep -Seconds 2
        $waited += 2

        $state = (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue).State
        if (-not $state -or $state -eq 'Running') { continue }

        # 0x41301 = 「任務仍在執行中」，不是最終結果。
        for ($settle = 0; $settle -lt 5; $settle++) {
            $info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
            if ($null -ne $info -and $info.LastTaskResult -ne 0x41301) {
                $lastResult = [int64]$info.LastTaskResult
                break
            }
            Start-Sleep -Seconds 1
            $waited += 1
        }
        break
    }

    $produced = @(Get-ChildItem -LiteralPath $BackupDir -File -ErrorAction SilentlyContinue |
                  Where-Object { $before -notcontains $_.FullName -and $_.Length -gt 0 })

    if ($lastResult -eq 0 -and $produced.Count -gt 0) {
        Write-Ok "試跑成功（退出碼 0）"
        foreach ($p in $produced) {
            Write-Info ("產出：{0}（{1:N2} MB）" -f $p.Name, ($p.Length / 1MB))
        }
    }
    elseif ($produced.Count -gt 0) {
        Write-Warn "退出碼為 $lastResult，但備份檔確實產出了 — 請看日誌確認。"
        foreach ($p in $produced) { Write-Info "產出：$($p.Name)" }
    }
    else {
        Write-Bad "試跑沒有產出備份檔（退出碼 $lastResult）。"
        $newest = Get-ChildItem -LiteralPath (Join-Path $BackupDir 'logs') -Filter '*.log' -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($newest) {
            Write-Host ''
            Write-Host "--- 最新日誌 $($newest.Name) 的結尾 ---" -ForegroundColor DarkGray
            Get-Content -LiteralPath $newest.FullName -Tail 20 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        }
    }
}

# ------------------------------------------------------------------ 總結

Write-Section '完成 — 這些東西放在哪裡'

Write-Host ''
Write-Host '  排程任務        ' -NoNewline; Write-Host $TaskName -ForegroundColor White
Write-Host '  連線設定        ' -NoNewline; Write-Host $ProfileName -ForegroundColor White
Write-Host ''
Write-Host '  備份檔          ' -NoNewline; Write-Host $BackupDir -ForegroundColor White
Write-Host '  還原前快照      ' -NoNewline; Write-Host (Join-Path $BackupDir 'snapshots') -ForegroundColor White
Write-Host '  執行日誌        ' -NoNewline; Write-Host (Join-Path $BackupDir 'logs') -ForegroundColor White
Write-Host ''
Write-Host '  備份腳本        ' -NoNewline; Write-Host $ScriptPath -ForegroundColor White
Write-Host '  pgbackup.exe    ' -NoNewline; Write-Host $CliPath -ForegroundColor White
if ($PgBinPath) {
    Write-Host '  客戶端工具      ' -NoNewline; Write-Host $PgBinPath -ForegroundColor White
}
Write-Host '  連線設定存放區  ' -NoNewline; Write-Host (Join-Path $env:ProgramData 'PostgresBackup') -ForegroundColor White
$systemHistory = Join-Path $env:SystemRoot 'System32\config\systemprofile\AppData\Local\PostgresBackup\history.db'
Write-Host '  排程稽核歷史    ' -NoNewline; Write-Host $systemHistory -ForegroundColor White
Write-Host ''
Write-Info '排程的稽核歷史不會出現在圖形介面的「備份歷史」頁面——那裡讀的是你登入帳號的那一份。'
Write-Host ''
Write-Host '  日後要檢查排程跑得好不好（' -NoNewline
Write-Host '不需要系統管理員權限' -ForegroundColor Green -NoNewline
Write-Host '）：'
Write-Host "    .\check-backup-status.ps1 -BackupDir `"$BackupDir`" -TaskName `"$TaskName`"" -ForegroundColor White
Write-Info '它會報告排程狀態、上次退出碼、最近的備份檔、日誌結尾與磁碟空間。'
Write-Host ''
Write-Host '  保留政策：備份 ' -NoNewline
Write-Host "$RetentionDays 天" -ForegroundColor White -NoNewline
Write-Host ' / 快照 ' -NoNewline
Write-Host "$SnapshotRetentionDays 天" -ForegroundColor White -NoNewline
Write-Host ' / 日誌 30 天'
Write-Host ''
Write-Host '  日後要移除：' -ForegroundColor DarkGray
Write-Host "    Unregister-ScheduledTask -TaskName `"$TaskName`" -Confirm:`$false" -ForegroundColor DarkGray
Write-Host "    & `"$CliPath`" profile remove --name `"$ProfileName`"" -ForegroundColor DarkGray
Write-Host ''
