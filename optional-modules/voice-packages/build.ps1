param([Parameter(Mandatory=$true)][string]$CoreBuild,[Parameter(Mandatory=$true)][string]$OutputDirectory,[Parameter(Mandatory=$true)][string]$Package,[string]$PythonPath='python')
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$core=Join-Path (Resolve-Path -LiteralPath $CoreBuild).Path 'agent语音.exe'
$output=Join-Path $OutputDirectory 'modules\voice-packages'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:x64 ('/win32manifest:'+(Join-Path $repo 'ui/app.manifest')) ('/out:'+(Join-Path $output 'module.exe')) ('/r:'+$core) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'ModuleWindow.cs')
if($LASTEXITCODE -ne 0){throw 'Optional module compilation failed'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'backend') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $output -Force
& $PythonPath (Join-Path $repo 'tools\module_packages.py') feature --root $OutputDirectory --output $Package --module-version 1.1.0
if($LASTEXITCODE -ne 0){throw 'Optional module packaging failed'}
