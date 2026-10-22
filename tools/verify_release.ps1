param([string]$ReleaseDirectory=$PSScriptRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ReleaseDirectory).Path.TrimEnd('\')
$manifestName=(-join [char[]](0x53D1,0x884C,0x6E05,0x5355))+'.json'
$manifest=Get-Content -LiteralPath (Join-Path $root $manifestName) -Raw -Encoding UTF8 | ConvertFrom-Json
if($manifest.format -ne 'agent-voice-release' -or $manifest.version -ne 1 -or !$manifest.files){throw 'Unsupported release manifest'}
$index=0
foreach($entry in $manifest.files){
    $relative=[string]$entry.path
    if([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or ($relative -split '[/\\]') -contains '..'){throw 'Invalid release path'}
    $path=[IO.Path]::GetFullPath((Join-Path $root $relative))
    if(!$path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Release path escapes directory'}
    $index++;Write-Progress -Activity 'Verify release packages' -Status $relative -PercentComplete ([int](100*$index/$manifest.files.Count))
    $file=Get-Item -LiteralPath $path
    if($file.PSIsContainer -or $file.Length -ne $entry.bytes){throw ('File missing or wrong size: '+$relative)}
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw ('SHA-256 mismatch: '+$relative)}
    Write-Output ('PASS '+$relative)
}
Write-Progress -Activity 'Verify release packages' -Completed
Write-Output 'Package integrity verified. Install and test synthesis/playback separately.'
