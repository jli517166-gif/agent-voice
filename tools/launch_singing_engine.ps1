$ErrorActionPreference = 'Stop'
$engineRoot = Split-Path -Parent $PSScriptRoot
$portableRoot = Join-Path $engineRoot 'portable'
$launcher = Join-Path $portableRoot 'start_gradio_ui.bat'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw "ACE-Step 启动文件不存在：$launcher" }
if (-not (Test-Path -LiteralPath (Join-Path $portableRoot 'checkpoints\acestep-v15-turbo') -PathType Container)) { throw 'ACE-Step v1.5 Turbo 模型缺失' }

# The release deliberately excludes the installed machine's .env.  These are
# reproducible local-only defaults; users may create their own .env afterward.
$env:CHECK_UPDATE = 'false'
$env:SERVER_NAME = '127.0.0.1'
$env:PORT = '7860'
$env:CONFIG_PATH = '--config_path acestep-v15-turbo'
$env:INIT_LLM = '--init_llm false'
$env:ACESTEP_CONFIG_PATH = 'acestep-v15-turbo'
$env:ACESTEP_INIT_LLM = 'false'

$watcher = Join-Path $PSScriptRoot 'wait_and_open_singing.ps1'
if (Test-Path -LiteralPath $watcher) {
    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$watcher+'"')) | Out-Null
}
Push-Location $portableRoot
try { & $launcher } finally { Pop-Location }
