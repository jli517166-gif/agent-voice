param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $sourceRoot 'dist' }
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiledExecutable = Join-Path $OutputDirectory 'agent-voice-build.exe'
$finalExecutable = Join-Path $OutputDirectory ('agent' + [char]0x8BED + [char]0x97F3 + '.exe')
$arguments = @('/nologo','/target:winexe','/optimize+','/platform:x64',('/out:' + $compiledExecutable),'/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:Microsoft.CSharp.dll',('/win32manifest:' + (Join-Path $sourceRoot 'ui\app.manifest')))
$sources = Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'ui') -Filter '*.cs' | ForEach-Object FullName
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Native UI compilation failed' }
Move-Item -LiteralPath $compiledExecutable -Destination $finalExecutable -Force
foreach ($folder in @('service','integrations','tools')) {
    $target = Join-Path $OutputDirectory $folder
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot $folder) -File | Where-Object { $folder -ne 'tools' -or $_.Name -in @('prepare_airi.py','release_audit.py','export_connector.py','config_bundle.py','tts_bundle.py','connect_harness.py') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name) -Force }
}
Get-ChildItem -LiteralPath $sourceRoot -Filter '*.md' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $OutputDirectory $_.Name) -Force }
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'models'),(Join-Path $OutputDirectory 'voices'),(Join-Path $OutputDirectory 'voice-model-packages'),(Join-Path $OutputDirectory 'data'),(Join-Path $OutputDirectory 'tts-engines'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice\source'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice\runtime\tools'),(Join-Path $OutputDirectory 'tts-engines\cosyvoice\models') -Force | Out-Null
Write-Output $finalExecutable
