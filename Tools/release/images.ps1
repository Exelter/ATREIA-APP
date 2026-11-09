# Puts the game's pictures into a built or published folder, for the self-checks and the screenshots: the image pack the app downloads on its first
# start (Client/Update/ImagePack.cs), fetched from atreia.app, its SHA-256 checked, unpacked into <Dir>\assets (aion2\..., classes\icons, races\icons).
# The pictures are not in this repository (it is public; they are NCSOFT's) and a release never carries them.
#
# Usage: pwsh Tools/release/images.ps1 -Dir out [-Index https://atreia.app/app/updates/images.json]
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [string]$Index = 'https://atreia.app/app/updates/images.json'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$agent = 'AtreiaApp-checks'

try {
    $pack = Invoke-RestMethod -Uri $Index -UserAgent $agent -TimeoutSec 60
} catch {
    "::error title=Image pack::The image pack is not on atreia.app yet (${Index}: $($_.Exception.Message)). Put it there with the site's tools/app-images.mjs, then DEPLOY."
    exit 1
}

$url = if ($pack.url) { $pack.url } else { [Uri]::new([Uri]$Index, [string]$pack.file).AbsoluteUri }
$zip = Join-Path ([IO.Path]::GetTempPath()) "atreia-images-$($pack.version).zip"
Invoke-WebRequest -Uri $url -OutFile $zip -UserAgent $agent -TimeoutSec 900
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne ([string]$pack.sha256).ToLowerInvariant()) {
    "::error title=Image pack::$url does not match the SHA-256 in $Index ($hash, not $($pack.sha256))."
    exit 1
}

$assets = Join-Path (Resolve-Path $Dir).Path 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
# Windows' own tar unpacks zip files, and much faster than Expand-Archive
tar -xf $zip -C $assets
if ($LASTEXITCODE -ne 0) { throw "tar could not unpack $zip ($LASTEXITCODE)" }
Remove-Item $zip
$count = @(Get-ChildItem $assets -Recurse -File -Include *.png, *.jpg, *.webp).Count
"Image pack $($pack.version) ($($pack.files) pictures, $([math]::Round($pack.size / 1MB)) MB) unpacked into $assets ($count pictures there now)."
