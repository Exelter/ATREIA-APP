# Builds a release of Atreia App, everything up to the files that go on atreia.app: the same on GitHub (.github/workflows/release.yml) and on the
# owner's PC without GitHub (9 November 2026, the owner: "a backup of everything, so if GitHub gets banned I can still update the app"; the website's
# APP-BUILD.bat runs this, then puts the result on atreia.app with its tools/app-release.mjs).
#
#   1. publish     the app in the layout it is installed in (Tools/release/publish.ps1)
#   2. checks      the app's own self-checks on a copy of it, with the game's pictures from atreia.app (Tools/release/images.ps1, selftest.ps1)
#   3. previous    the release before it from atreia.app, so the update can be a small delta package (skipped with -NoDelta, or when there is none)
#   4. pack        Velopack: the installer, the full package (and the delta), the update index releases.<channel>.json
#   5. tidy        the installer named after the app (Atreia-App-Setup.exe: the setup wizard, installer/atreia.iss, around Velopack's), the update notes
#                  (updates.json) and SHA256SUMS.txt beside them
#
# A version with a suffix ("1.22.0-Beta") is a test release: packed on its own channel ("test"), so installed copies on the normal channel never get it.
# Needs the .NET 10 SDK and PowerShell 7; the Velopack CLI (vpk 1.2.0) is installed when it is missing.
#
# Usage (from anywhere): pwsh Tools/release/build-release.ps1 -Version 1.21.5 [-Out releases] [-NoDelta] [-SkipChecks]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Out = 'releases',
    [switch]$NoDelta,
    [switch]$SkipChecks
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "`"$Version`" is no version: write it like 1.21.5, or 1.22.0-Beta for a test release." }
$number = ($Version -split '-', 2)[0]
$channel = if ($Version -eq $number) { 'stable' } else { 'test' }

# everything relative to the repository's root, wherever this was started from; the output folder as given (relative to where it was started)
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Out = [IO.Path]::GetFullPath($Out, (Get-Location).Path)
$work = Join-Path $root 'release-work'
Push-Location $root
try {
    foreach ($tool in 'dotnet') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not installed: install the .NET 10 SDK (winget install Microsoft.DotNet.SDK.10) and run this again." }
    }
    Remove-Item $work, $Out -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $work, $Out | Out-Null
    $publish = Join-Path $work 'publish'

    "==== 1/5 Publishing Atreia App $Version ($channel) ===="
    & (Join-Path $PSScriptRoot 'publish.ps1') -Version $number -Out $publish

    if ($SkipChecks) {
        "==== 2/5 Self-checks: skipped (-SkipChecks) ===="
    } else {
        "==== 2/5 Self-checks ===="
        $checked = Join-Path $work 'checked'
        Copy-Item $publish $checked -Recurse
        $global:LASTEXITCODE = 0
        & (Join-Path $PSScriptRoot 'images.ps1') -Dir $checked
        if ($LASTEXITCODE -ne 0) { throw 'The game pictures for the self-checks could not be fetched from atreia.app.' }
        Push-Location $work
        try {
            $global:LASTEXITCODE = 0
            & (Join-Path $PSScriptRoot 'selftest.ps1') -Dir $checked -Runtime
            if ($LASTEXITCODE -ne 0) { throw 'A self-check failed: nothing is packed (the failed lines are above).' }
        } finally { Pop-Location }
        Remove-Item $checked -Recurse -Force
    }

    # the Velopack CLI, the version the releases were always packed with
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        "Installing the Velopack CLI (vpk 1.2.0)..."
        dotnet tool install --global vpk --version 1.2.0 | Out-Host
        $tools = Join-Path $HOME '.dotnet/tools'
        if ($env:PATH -notlike "*$tools*") { $env:PATH = "$tools$([IO.Path]::PathSeparator)$env:PATH" }
        if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw 'The Velopack CLI (vpk) could not be installed.' }
    }

    if ($NoDelta) {
        "==== 3/5 The previous release: skipped (-NoDelta), the update is a full download ===="
    } else {
        "==== 3/5 The previous release, for a small update package ===="
        # allowed to fail: with no earlier release on this channel the update is only a full download, never wrong
        vpk download http --url https://atreia.app/app/updates/ --channel $channel --outputDir $Out | Out-Host
        if ($LASTEXITCODE -ne 0) { "atreia.app has no earlier release on the $channel channel: this one is a full download only." }
    }

    "==== 4/5 Packing ===="
    vpk pack --packId AtreiaApp --packTitle 'Atreia App' --packAuthors 'Atreia' --packVersion $Version --packDir $publish --mainExe 'AtreiaApp.exe' `
        --icon (Join-Path $root 'Client/assets/app/aiondps.ico') --channel $channel --outputDir $Out | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)." }

    "==== 5/5 The files for atreia.app ===="
    $name = if ($channel -eq 'stable') { 'Atreia-App-Setup.exe' } else { "Atreia-App-$channel-Setup.exe" }
    $velopackSetup = Join-Path $work "AtreiaApp-$channel-Setup.exe"
    Move-Item (Join-Path $Out "AtreiaApp-$channel-Setup.exe") $velopackSetup -Force
    # What players download is the setup wizard around Velopack's installer (Tools/release/installer/atreia.iss, Inno Setup 6; 11 November 2026):
    # Inno Setup is installed on GitHub when it is missing; on a PC without it the installer is Velopack's own one-click one, as before.
    function Find-Iscc {
        $found = @()
        $cmd = Get-Command iscc -ErrorAction SilentlyContinue
        if ($cmd) { $found += $cmd.Source }
        foreach ($dir in ${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')) {
            if ($dir) { $found += (Join-Path $dir 'Inno Setup 6\ISCC.exe') }
        }
        $found | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    $iscc = Find-Iscc
    if (-not $iscc -and $env:GITHUB_ACTIONS -eq 'true') {
        "Installing Inno Setup 6..."
        choco install innosetup -y --no-progress | Out-Host
        $iscc = Find-Iscc
    }
    if ($iscc) {
        "The setup wizard ($iscc)..."
        $global:LASTEXITCODE = 0
        & $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$($number).0" "/DVelopackSetup=$velopackSetup" "/DOutputDir=$Out" `
            "/DOutputName=$([IO.Path]::GetFileNameWithoutExtension($name))" (Join-Path $PSScriptRoot 'installer/atreia.iss') | Out-Host
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $Out $name))) { throw "The setup wizard could not be built (ISCC exit code $LASTEXITCODE)." }
    } else {
        "Inno Setup 6 is not on this PC: the installer is Velopack's own one-click one. For the setup wizard: winget install JRSoftware.InnoSetup"
        Copy-Item $velopackSetup (Join-Path $Out $name)
    }
    # the portable zip is another full copy of what the installer carries; the previous release's full package was only needed for the delta
    Get-ChildItem $Out -File -Filter '*-Portable.zip' | Remove-Item
    Get-ChildItem $Out -File -Filter '*.nupkg' | Where-Object { $_.Name -notlike "AtreiaApp-$Version-*" } | Remove-Item
    Copy-Item (Join-Path $root 'Client/assets/changelog/updates.json') (Join-Path $Out 'updates.json')
    $sums = Get-ChildItem $Out -File | Where-Object { $_.Name -like '*.exe' -or $_.Name -like '*.nupkg' } | Sort-Object Name |
        ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
    [IO.File]::WriteAllText((Join-Path $Out 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"))
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem $Out -File | ForEach-Object { "  $($_.Name)  $([math]::Round($_.Length / 1MB, 1)) MB" }
    "Atreia App $Version ($channel) is built in $Out"
} finally {
    Pop-Location
}
