param([string]$BuildDirectory='dist',[string]$OutputDirectory='acceptance/first-run')
$ErrorActionPreference='Stop'
$built=(Resolve-Path -LiteralPath $BuildDirectory).Path
$exeName='agent'+[char]0x8bed+[char]0x97f3+'.exe'
$exe=Join-Path $built $exeName
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output=(Resolve-Path -LiteralPath $OutputDirectory).Path
$root=Join-Path $output ('fixture-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $root 'assets') -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $root $exeName)
[Reflection.Assembly]::LoadFrom($exe) | Out-Null
$voice=New-Object AgentVoice.VoiceConfig
$voice.baseModel='tts-engines/cosyvoice/models/Fairy'
$voice.reference='voices/Fairy/reference.wav'
$voice.referenceText='factory fixture'
$preset=@{version=1;name='Fairy';voice=$voice}
$preset | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'assets/bundled-voice.json') -Encoding UTF8
$settingsFile=Join-Path $root 'data/settings.json'
function Launch-Fixture {
    $process=Start-Process -FilePath (Join-Path $root $exeName) -WorkingDirectory $root -WindowStyle Hidden -PassThru
    try {
        $deadline=[DateTime]::UtcNow.AddSeconds(10)
        while(!(Test-Path -LiteralPath $settingsFile)){
            if($process.HasExited){throw 'UI exited before creating first-run settings'}
            if([DateTime]::UtcNow -gt $deadline){throw 'UI first-run timeout'}
            Start-Sleep -Milliseconds 100
        }
        Start-Sleep -Milliseconds 400
        return Get-Content -LiteralPath $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    } finally {
        if(!$process.HasExited){$process.CloseMainWindow() | Out-Null;if(!$process.WaitForExit(500)){Stop-Process -InputObject $process -Force;$process.WaitForExit()}}
        $process.Dispose()
    }
}
$first=Launch-Fixture
if($first.voice.baseModel -ne $voice.baseModel -or $first.profiles[0].name -ne 'Fairy' -or $first.airi.enabled -or $first.token.Length -lt 24){throw 'First launch did not apply factory voice safely'}
$first.voice.speed=1.1
$first | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsFile -Encoding UTF8
Set-Content -LiteralPath (Join-Path $root 'assets/bundled-voice.json') -Value 'invalid' -Encoding UTF8
$again=Launch-Fixture
if($again.voice.speed -ne 1.1 -or $again.token -ne $first.token){throw 'Existing settings changed during launch'}
@{ok=$true;checks=@('actual exe first launch','factory Fairy','AIRI disabled','existing settings and credential preserved')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'first-run-results.json') -Encoding UTF8
