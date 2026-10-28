param(
    [switch]$InstallSystemComponents,
    [string]$SupportRoot = $PSScriptRoot,
    [string]$MinimumNvidiaDriver = '551.61'
)

$ErrorActionPreference = 'Stop'

function Test-VersionAtLeast([string]$Current, [string]$Required) {
    try { return [version]$Current -ge [version]$Required } catch { return $false }
}

function Find-Installer([string]$Name) {
    Get-ChildItem -LiteralPath $SupportRoot -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}

function Install-IfMissing([string]$Label, [bool]$Present, [string]$FileName, [string]$Arguments) {
    if ($Present) { Write-Host "[通过] $Label" -ForegroundColor Green; return $true }
    $installer = Find-Installer $FileName
    if (-not $InstallSystemComponents) {
        Write-Host "[缺少] $Label。请以管理员身份重新运行：PowerShell -ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallSystemComponents" -ForegroundColor Yellow
        return $false
    }
    if (-not $installer) { Write-Host "[缺少安装器] $Label：$FileName" -ForegroundColor Red; return $false }
    Write-Host "[安装] $Label" -ForegroundColor Cyan
    $process = Start-Process -FilePath $installer -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010, 1641) { throw "$Label 安装失败，退出码 $($process.ExitCode)" }
    if ($process.ExitCode -in 3010, 1641) { Write-Host '[需要重启] 安装器要求重新启动 Windows。' -ForegroundColor Yellow }
    return $true
}

if (-not [Environment]::Is64BitOperatingSystem) { throw '只支持 Windows 10/11 64 位系统。' }
Write-Host '[通过] Windows 64 位' -ForegroundColor Green

$netRelease = 0
try { $netRelease = [int](Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' Release) } catch {}
$dotnetOk = $netRelease -ge 528040
$vcOk = (Test-Path -LiteralPath "$env:WINDIR\System32\vcruntime140.dll") -and (Test-Path -LiteralPath "$env:WINDIR\System32\msvcp140.dll")
$webviewOk = $false
foreach ($key in @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
)) { if (Test-Path $key) { $webviewOk = $true; break } }
if (-not $webviewOk) { $webviewOk = Test-Path -LiteralPath "${env:ProgramFiles(x86)}\Microsoft\EdgeWebView\Application" }

$dotnetReady = Install-IfMissing '.NET Framework 4.8' $dotnetOk 'NDP48-x86-x64-AllOS-ENU.exe' '/q /norestart'
$vcReady = Install-IfMissing 'Microsoft Visual C++ x64 运行库' $vcOk 'VC_redist.x64.exe' '/install /quiet /norestart'
$webviewReady = Install-IfMissing 'Microsoft Edge WebView2 Runtime' $webviewOk 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe' '/silent /install'

$driver = ''
try {
    $driver = (& nvidia-smi --query-gpu=driver_version --format=csv,noheader,nounits 2>$null | Select-Object -First 1).Trim()
} catch {}
if (-not $driver) {
    Write-Host "[缺少] 未检测到 NVIDIA 驱动；离线环境包最低要求 $MinimumNvidiaDriver。显卡驱动必须按目标电脑型号从 NVIDIA 官方安装。" -ForegroundColor Red
    $driverReady = $false
} elseif (-not (Test-VersionAtLeast $driver $MinimumNvidiaDriver)) {
    Write-Host "[不兼容] 当前 NVIDIA 驱动 $driver，最低要求 $MinimumNvidiaDriver。请先更新驱动。" -ForegroundColor Red
    $driverReady = $false
} else {
    Write-Host "[通过] NVIDIA 驱动 $driver" -ForegroundColor Green
    $driverReady = $true
}

if ($InstallSystemComponents) {
    Write-Host '系统组件安装命令已经执行。若上方提示需要重启，请重启后重新运行本检查。' -ForegroundColor Cyan
}
if (-not ($dotnetReady -and $vcReady -and $webviewReady -and $driverReady)) { exit 2 }
Write-Host '安装前检查通过。接下来先解压软件本体，再分别导入环境包和所需的 TTS 引擎包；最后在配置流程执行环境检查与实际生成验证。' -ForegroundColor Green

