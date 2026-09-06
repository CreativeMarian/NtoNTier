<#
============================================================
  NtoNServer · NtoNTier 服务端管理脚本（supernode 一键管理）
------------------------------------------------------------
  用法（由各 .bat 调用，也可直接命令行使用）:
    .\NtoNServer.ps1 -Start       启动服务端（后台，写 PID/日志）
    .\NtoNServer.ps1 -Stop        停止服务端
    .\NtoNServer.ps1 -Restart     重启
    .\NtoNServer.ps1 -Status      查看运行状态
    .\NtoNServer.ps1 -Watch       守护模式（进程死了自动重启，配合开机自启用）
    .\NtoNServer.ps1 -Log         查看最近日志
    .\NtoNServer.ps1 -Firewall    放行防火墙端口
    .\NtoNServer.ps1 -AutoStartOn 设置开机自启（登录时后台运行 + 守护）
    .\NtoNServer.ps1 -AutoStartOff 取消开机自启
  参数: -Port 3301  -Verbose(-v 详细日志)  -MgmtPort 0(可选管理端口)
============================================================
#>
param(
    [switch]$Start, [switch]$Stop, [switch]$Restart, [switch]$Status,
    [switch]$Watch, [switch]$Log, [switch]$Firewall,
    [switch]$AutoStartOn, [switch]$AutoStartOff,
    [int]$Port = 3301,
    [int]$MgmtPort = 0,
    [switch]$Verbose
)
$ErrorActionPreference = "SilentlyContinue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Supernode = Join-Path $ScriptDir "supernode.exe"
$PidFile   = Join-Path $ScriptDir "supernode.pid"
$OutLog    = Join-Path $ScriptDir "supernode.log"
$ErrLog    = Join-Path $ScriptDir "supernode.log.err"
$TaskName  = "NtoNServer"

function Get-SupernodeProc {
    if (Test-Path $PidFile) {
        $pid2 = (Get-Content $PidFile -ErrorAction SilentlyContinue | Select-Object -First 1)
        if ($pid2) {
            $p = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
            if ($p -and $p.ProcessName -eq "supernode") { return $p }
        }
    }
    $p = Get-Process -Name "supernode" -ErrorAction SilentlyContinue | Select-Object -First 1
    return $p
}

function Do-Start {
    $p = Get-SupernodeProc
    if ($p) { Write-Host "[已运行] supernode 已在运行 (PID: $($p.Id))" -ForegroundColor Yellow; return }
    if (-not (Test-Path $Supernode)) { Write-Host "[错误] 找不到 supernode.exe，请确认本脚本与其在同一目录" -ForegroundColor Red; exit 1 }

    $argList = @("-p", $Port)
    if ($MgmtPort -gt 0) { $argList += @("-t", $MgmtPort) }
    if ($Verbose) { $argList += "-v" }

    $proc = Start-Process -FilePath $Supernode -ArgumentList $argList `
        -WorkingDirectory $ScriptDir -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $OutLog -RedirectStandardError $ErrLog
    Set-Content -Path $PidFile -Value $proc.Id -Encoding ASCII
    Start-Sleep -Milliseconds 800
    if ($proc.HasExited) {
        Write-Host "[失败] supernode 启动后立即退出，日志如下：" -ForegroundColor Red
        Do-Log
        exit 1
    }
    Write-Host "[OK] supernode 已启动" -ForegroundColor Green
    Write-Host "  监听端口: $Port (UDP)" -ForegroundColor Cyan
    Write-Host "  PID: $($proc.Id)" -ForegroundColor DarkGray
    Write-Host "  日志: $OutLog" -ForegroundColor DarkGray
}

function Do-Stop {
    $p = Get-SupernodeProc
    if (-not $p) { Write-Host "[提示] supernode 未在运行" -ForegroundColor Yellow; return }
    try { Stop-Process -Id $p.Id -Force -ErrorAction Stop; Write-Host "[OK] 已停止 supernode (PID: $($p.Id))" -ForegroundColor Green } catch { Write-Host "[错误] 停止失败: $_" -ForegroundColor Red }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
}

function Do-Status {
    $p = Get-SupernodeProc
    if ($p) {
        Write-Host "[运行中] supernode PID=$($p.Id)  内存=$([math]::Round($p.WorkingSet64/1MB,1))MB" -ForegroundColor Green
        $r = Test-NetConnection -ComputerName 127.0.0.1 -Port $Port -WarningAction SilentlyContinue
        Write-Host ("  端口 {0} (UDP监听): {1}" -f $Port, ($(if ($r.TcpTestSucceeded) { "可连接" } else { "本地TCP探测未成功(仅UDP时属正常)" })))
    } else {
        Write-Host "[已停止] supernode 未在运行" -ForegroundColor Yellow
    }
}

function Do-Log {
    if (Test-Path $OutLog) {
        Write-Host "----- supernode.log (最近 40 行) -----" -ForegroundColor Cyan
        Get-Content $OutLog -Tail 40
    } else { Write-Host "[提示] 尚无日志文件" -ForegroundColor Yellow }
}

function Do-Watch {
    Write-Host "[守护] NtoNServer 守护模式启动，每 10 秒检查一次，进程退出会自动重启" -ForegroundColor Cyan
    while ($true) {
        $p = Get-SupernodeProc
        if (-not $p) {
            Write-Host ("[{0}] 检测到进程未运行，正在重启..." -f (Get-Date -Format "HH:mm:ss")) -ForegroundColor Yellow
            Do-Start
        }
        Start-Sleep -Seconds 10
    }
}

function Do-Firewall {
    $r1 = "NtoNServer UDP"; $r2 = "NtoNServer TCP"
    & netsh advfirewall firewall delete rule name=$r1 | Out-Null
    & netsh advfirewall firewall delete rule name=$r2 | Out-Null
    & netsh advfirewall firewall add rule name=$r1 dir=in action=allow protocol=UDP localport=$Port | Out-Null
    & netsh advfirewall firewall add rule name=$r2 dir=in action=allow protocol=TCP localport=$Port | Out-Null
    Write-Host "[OK] 防火墙已放行端口 $Port (UDP/TCP)" -ForegroundColor Green
}

function Do-AutoStartOn {
    $scriptPath = Join-Path $ScriptDir "NtoNServer.ps1"
    $arg = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$scriptPath`" -Watch"
    $action  = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arg
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Force | Out-Null
    Write-Host "[OK] 已设置开机自启（登录后自动后台运行并守护）" -ForegroundColor Green
    Write-Host "  任务名: $TaskName （可用 任务计划程序 查看）" -ForegroundColor DarkGray
}

function Do-AutoStartOff {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "[OK] 已取消开机自启" -ForegroundColor Green
}

# ============ 入口 ============
if ($Start)     { Do-Start }
elseif ($Stop)  { Do-Stop }
elseif ($Restart) { Do-Stop; Start-Sleep -Seconds 1; Do-Start }
elseif ($Status)  { Do-Status }
elseif ($Watch)   { Do-Watch }
elseif ($Log)     { Do-Log }
elseif ($Firewall){ Do-Firewall }
elseif ($AutoStartOn) { Do-AutoStartOn }
elseif ($AutoStartOff){ Do-AutoStartOff }
else {
    Write-Host "NtoNServer 用法：" -ForegroundColor Cyan
    Write-Host "  -Start / -Stop / -Restart / -Status / -Log"
    Write-Host "  -Watch（守护） / -Firewall / -AutoStartOn / -AutoStartOff"
}
