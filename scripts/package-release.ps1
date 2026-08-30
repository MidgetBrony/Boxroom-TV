param(
    [string]$Configuration = "Release",
    [string]$Version = "3.3.0"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$vlcUnity = Join-Path (Split-Path -Parent $repo) "vlc-unity"
$libVlcSharp = Join-Path (Split-Path -Parent $repo) "LibVLCSharp"
$stage = Join-Path $repo "dist\stage"
$archive = Join-Path $repo "dist\Boxroom-TV-$Version.zip"

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

$paths = @(
    "Mods",
    "BOXROOM_Data\Plugins\x86_64\plugins",
    "UserData\Boxroom-TV\Tools",
    "UserData\Boxroom-TV\Licenses"
)
foreach ($path in $paths) { New-Item -ItemType Directory -Path (Join-Path $stage $path) -Force | Out-Null }

Copy-Item -LiteralPath (Join-Path $repo "bin\$Configuration\netstandard2.1\Boxroom_TV.dll") -Destination (Join-Path $stage "Mods\Boxroom_TV.dll")
Copy-Item -LiteralPath (Join-Path $libVlcSharp "src\LibVLCSharp\bin\Release\netstandard2.0\LibVLCSharp.dll") -Destination (Join-Path $stage "Mods\LibVLCSharp.dll")
Copy-Item -LiteralPath (Join-Path $vlcUnity "build_windows_local\PluginSource\libVLCUnityPlugin.dll") -Destination (Join-Path $stage "BOXROOM_Data\Plugins\x86_64\VLCUnityPlugin.dll")
$vlc = Join-Path $vlcUnity ".build-tools\vlc\vlc-4.0.0-dev"
Copy-Item -LiteralPath (Join-Path $vlc "libvlc.dll") -Destination (Join-Path $stage "BOXROOM_Data\Plugins\x86_64\libvlc.dll")
Copy-Item -LiteralPath (Join-Path $vlc "libvlccore.dll") -Destination (Join-Path $stage "BOXROOM_Data\Plugins\x86_64\libvlccore.dll")
Copy-Item -Path (Join-Path $vlc "plugins\*") -Destination (Join-Path $stage "BOXROOM_Data\Plugins\x86_64\plugins") -Recurse
Copy-Item -LiteralPath (Join-Path $repo "Tools\yt-dlp.exe") -Destination (Join-Path $stage "UserData\Boxroom-TV\Tools\yt-dlp.exe")
Copy-Item -LiteralPath (Join-Path $repo "THIRD-PARTY-NOTICES.md") -Destination (Join-Path $stage "UserData\Boxroom-TV\Licenses")
Copy-Item -Path (Join-Path $repo "Licenses\*") -Destination (Join-Path $stage "UserData\Boxroom-TV\Licenses")
Copy-Item -LiteralPath (Join-Path $vlc "AUTHORS.txt") -Destination (Join-Path $stage "UserData\Boxroom-TV\Licenses\VLC-AUTHORS.txt")
Copy-Item -LiteralPath (Join-Path $vlc "COPYING.txt") -Destination (Join-Path $stage "UserData\Boxroom-TV\Licenses\VLC-COPYING.txt")

if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $repo "dist\SHA256SUMS.txt") -Value "$hash  $([IO.Path]::GetFileName($archive))" -Encoding utf8
Write-Output $archive
