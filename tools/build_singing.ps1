param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output=Join-Path $OutputDirectory 'Agent唱歌.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:x64 "/out:$output" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll (Join-Path $root 'singing-ui\Program.cs')
if($LASTEXITCODE -ne 0){throw 'Agent Singing compilation failed'}
Copy-Item -LiteralPath $output -Destination (Join-Path $OutputDirectory 'module.exe') -Force
Write-Output $output
