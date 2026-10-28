param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$BuildRoot,
    [Parameter(Mandatory = $true)][string]$RuntimeRoot,
    [Parameter(Mandatory = $true)][string]$Python,
    [string]$Version = '0.2.0-preview.8',
    [string]$MinimumNvidiaDriver = '551.61',
    [string]$DotNetInstaller = ''
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$targetPath = [IO.Path]::GetFullPath($Target)
$buildPath = [IO.Path]::GetFullPath($BuildRoot)
$runtimePath = [IO.Path]::GetFullPath($RuntimeRoot)
if (Test-Path -LiteralPath $targetPath) { throw "目标已存在，为避免覆盖请使用新目录：$targetPath" }
foreach ($required in @(
    (Join-Path $buildPath 'agent语音.exe'),
    (Join-Path $runtimePath 'runtime\core\python.exe'),
    (Join-Path $runtimePath 'tts-engines\cosyvoice\source'),
    (Join-Path $runtimePath 'tts-engines\gpt-sovits\source'),
    $Python
)) { if (-not (Test-Path -LiteralPath $required)) { throw "缺少打包输入：$required" } }

$software = Join-Path $targetPath '01-软件本体'
$environment = Join-Path $targetPath '02-运行环境'
$engines = Join-Path $targetPath '03-TTS引擎'
$support = Join-Path $environment '首次启动支持'
$audit = Join-Path $targetPath '审计资料'
New-Item -ItemType Directory -Path $software,$environment,$engines,$support,$audit -Force | Out-Null

$moduleTool = Join-Path $sourceRoot 'tools\module_packages.py'
& $Python -B -X utf8 $moduleTool application --root $buildPath --output (Join-Path $software "AgentVoice-Windows-x64-$Version.zip") --application-folder 'Agent语音' --audit-directory $audit
if ($LASTEXITCODE -ne 0) { throw '软件本体打包失败' }
& $Python -B -X utf8 $moduleTool environment --root $runtimePath --output (Join-Path $environment "AgentVoice-Windows-x64-双引擎离线环境-$Version.ttsenvironment") --engines cosyvoice gpt-sovits --profile 'win-x64-cosy-cu121-gpt-cu124' --minimum-driver $MinimumNvidiaDriver
if ($LASTEXITCODE -ne 0) { throw '离线环境打包失败' }
& $Python -B -X utf8 $moduleTool engine --root $runtimePath --output (Join-Path $engines "CosyVoice-基础引擎-$Version.ttsengine") --engine cosyvoice
if ($LASTEXITCODE -ne 0) { throw 'CosyVoice 引擎打包失败' }
& $Python -B -X utf8 $moduleTool engine --root $runtimePath --output (Join-Path $engines "GPT-SoVITS-基础引擎-$Version.ttsengine") --engine gpt-sovits
if ($LASTEXITCODE -ne 0) { throw 'GPT-SoVITS 引擎打包失败' }

Copy-Item -LiteralPath (Join-Path $sourceRoot 'tools\offline_install_check.ps1') -Destination (Join-Path $support '检查并安装系统组件.ps1')
$systemPrerequisites = Join-Path $runtimePath 'tools\system-prerequisites'
if (Test-Path -LiteralPath $systemPrerequisites) { Get-ChildItem -LiteralPath $systemPrerequisites -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $support -Recurse -Force } }
if ($DotNetInstaller) {
    if (-not (Test-Path -LiteralPath $DotNetInstaller -PathType Leaf)) { throw "找不到 .NET 4.8 安装器：$DotNetInstaller" }
    Copy-Item -LiteralPath $DotNetInstaller -Destination (Join-Path $support 'NDP48-x86-x64-AllOS-ENU.exe') -Force
}
if (-not (Test-Path -LiteralPath (Join-Path $support 'NDP48-x86-x64-AllOS-ENU.exe'))) { throw '首次启动支持缺少 .NET Framework 4.8 离线安装器' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'docs\guides\模块化安装说明.md') -Destination (Join-Path $targetPath '安装说明.md')

$files = Get-ChildItem -LiteralPath $targetPath -File -Recurse | Where-Object { $_.Name -ne '包清单.json' } | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($targetPath.Length + 1).Replace('\','/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    format = 'agent-voice-modular-release'
    version = $Version
    createdAt = [DateTimeOffset]::Now.ToString('o')
    minimumNvidiaDriver = $MinimumNvidiaDriver
    containsVoicePackages = $false
    containsVoiceImportExportModule = $false
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $targetPath '包清单.json') -Encoding UTF8
Write-Output $targetPath

