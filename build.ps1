param([string]$OutputDirectory = '',[string]$Version = '0.1.1-preview.1')
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$webSdk = & (Join-Path $sourceRoot 'tools\restore_webview2.ps1') -Root $sourceRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $sourceRoot 'dist' }
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiledExecutable = Join-Path $OutputDirectory 'agent-voice-build.exe'
$finalExecutable = Join-Path $OutputDirectory ('agent' + [char]0x8BED + [char]0x97F3 + '.exe')
$arguments = @('/nologo','/target:winexe','/optimize+','/platform:x64',('/out:' + $compiledExecutable),'/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll','/r:Microsoft.CSharp.dll',('/win32manifest:' + (Join-Path $sourceRoot 'ui\app.manifest')))
$sources = Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'ui') -Filter '*.cs' | ForEach-Object FullName
$arguments += @(('/r:'+(Join-Path $webSdk 'lib\net462\Microsoft.Web.WebView2.Core.dll')),('/r:'+(Join-Path $webSdk 'lib\net462\Microsoft.Web.WebView2.WinForms.dll')))
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Native UI compilation failed' }
Move-Item -LiteralPath $compiledExecutable -Destination $finalExecutable -Force
foreach ($dependency in @('lib\net462\Microsoft.Web.WebView2.Core.dll','lib\net462\Microsoft.Web.WebView2.WinForms.dll','build\native\x64\WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $webSdk $dependency) -Destination (Join-Path $OutputDirectory (Split-Path $dependency -Leaf)) -Force
}
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'docs\licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $webSdk 'LICENSE.txt') -Destination (Join-Path $OutputDirectory 'docs\licenses\WebView2.txt') -Force
foreach ($legalFile in @('LICENSE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $legalFile) -Destination (Join-Path $OutputDirectory $legalFile) -Force
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'docs\legal-components.json') -Destination (Join-Path $OutputDirectory 'docs\legal-components.json') -Force
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'docs\licenses') -File -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $OutputDirectory 'docs\licenses') -Force }
foreach ($folder in @('service','integrations','tools')) {
    $target = Join-Path $OutputDirectory $folder
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot $folder) -File | Where-Object { $folder -ne 'tools' -or $_.Name -in @('software_update.py','prepare_airi.py','export_connector.py','connect_harness.py','verify_harness.py','install_mcp.py','diagnose.py','voice_profiles.py','provision_core_runtime.py','core-requirements.txt','prepare_training.py','prepare_gpt_resources.py','environment_repair.py','train_cosy_lora.py','prepare_cosy_tokens.py','privacy_tools.py') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name) -Force }
}
$agentAssets = Join-Path $sourceRoot '.agents'
if (Test-Path -LiteralPath (Join-Path $sourceRoot 'assets')) {
    $assetRoot = Join-Path $sourceRoot 'assets'
    Get-ChildItem -LiteralPath $assetRoot -File -Recurse | Where-Object { $_.Name -ne 'bundled-voice.json' } | ForEach-Object {
        $targetAsset = Join-Path (Join-Path $OutputDirectory 'assets') $_.FullName.Substring($assetRoot.Length + 1)
        New-Item -ItemType Directory -Path (Split-Path $targetAsset) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $targetAsset -Force
    }
}
if (Test-Path -LiteralPath $agentAssets) {
    $pluginTarget = Join-Path $OutputDirectory '.agents'
    New-Item -ItemType Directory -Path $pluginTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $agentAssets -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $pluginTarget -Recurse -Force }
}
foreach ($obsolete in @('integrations\opencode-plugin.mjs','integrations\pi-extension.ts','integrations\piper-requirements.txt','service\tts_bundle.py','service\config_bundle.py','tools\tts_bundle.py','tools\config_bundle.py','tools\release_audit.py','tools\module_packages.py','assets\bundled-voice.json','service\rvc_tone.py','service\rvc_accepted.py','service\rvc_index.py','service\moss_client.py','service\moss_worker.py','service\moss_audio.py','service\piper_client.py','service\piper_worker.py','MOSS引擎说明.md')) {
    $obsoletePath = Join-Path $OutputDirectory $obsolete
    if (Test-Path -LiteralPath $obsoletePath) { Remove-Item -LiteralPath $obsoletePath -Force }
}
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.md' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $OutputDirectory $_.Name) -Force }
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'docs') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'docs') -Filter '*.md' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path (Join-Path $OutputDirectory 'docs') $_.Name) -Force }
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'tts-engines\gpt-sovits') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'voices'),(Join-Path $OutputDirectory 'trained-voices'),(Join-Path $OutputDirectory 'data'),(Join-Path $OutputDirectory 'tts-engines'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice\source'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice\runtime\tools'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice\models') -Force | Out-Null
$manifestPython = $env:AGENT_VOICE_BUILD_PYTHON
if ([string]::IsNullOrEmpty($manifestPython)) { $manifestPython = (Get-Command python -ErrorAction Stop).Source }
& $manifestPython -B -X utf8 (Join-Path $sourceRoot 'tools\software_update.py') manifest --root $OutputDirectory --edition public --version $Version | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Application update manifest generation failed' }
Write-Output $finalExecutable
