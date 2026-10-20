param([string]$Root = (Split-Path $PSScriptRoot), [string]$Proxy = '')
$ErrorActionPreference = 'Stop'
$version = '1.0.3537.50'
$expected = '5EA526BBD728ADDA0DA4D31219267E96460494A427E4894C4E09D9F320F4B9AA'
$cache = Join-Path $Root '.build-deps\webview2'
$package = Join-Path $cache 'sdk.nupkg'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (!(Test-Path -LiteralPath $package)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $download = @{Uri="https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg";OutFile="$package.partial";UseBasicParsing=$true}
    if ($Proxy) { $download.Proxy = $Proxy }
    Invoke-WebRequest @download
    if ((Get-FileHash -LiteralPath "$package.partial" -Algorithm SHA256).Hash -ne $expected) { throw 'WebView2 SDK download checksum mismatch' }
    Move-Item -LiteralPath "$package.partial" -Destination $package -Force
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) { throw 'WebView2 SDK cache checksum mismatch; preserve the file and restore the pinned package' }
$sdk = Join-Path $cache $version
New-Item -ItemType Directory -Path $sdk -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($name in @('lib/net462/Microsoft.Web.WebView2.Core.dll','lib/net462/Microsoft.Web.WebView2.WinForms.dll','build/native/x64/WebView2Loader.dll','LICENSE.txt')) {
        $entry=$archive.GetEntry($name)
        if (!$entry) { throw "Missing SDK entry: $name" }
        $target=Join-Path $sdk $name
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$true)
    }
} finally { $archive.Dispose() }
Write-Output $sdk
