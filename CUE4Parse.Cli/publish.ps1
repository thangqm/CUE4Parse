#!/usr/bin/env pwsh
# Publishes cue4.exe as a self-contained single file.
# Trimming and NativeAOT are deliberately NOT used: ObjectTypeRegistry reflects
# over the assembly at static init, and trimming breaks UObject construction.

param(
    [string] $Runtime = "win-x64",
    [string] $Output  = "./artifacts",
    # Publish even without ACL. Such a binary cannot decode ACL-compressed animations.
    [switch] $AllowMissingNatives,
    # Leave Oodle/zlib/Detex out of the output; the binary downloads them on first use.
    [switch] $SkipNativeSidecars
)

$ErrorActionPreference = "Stop"

# CUE4Parse.csproj shells out to `cmake` and treats failure as non-fatal, so no CMake on
# PATH silently publishes a binary reporting native.library:false. VS ships its own.
if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -property installationPath
        if ($vsPath) {
            $vsCMake = Join-Path $vsPath "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin"
            if (Test-Path (Join-Path $vsCMake "cmake.exe")) {
                $env:PATH = "$vsCMake;$env:PATH"
                Write-Host "Using CMake bundled with Visual Studio: $vsCMake"
            }
        }
    }
}

if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    Write-Warning "cmake not found. CUE4Parse-Natives will not be built and ACL animations will fail."
}

# An uninitialised ACL submodule still builds a library, just without WITH_ACL.
$aclIncludes = Join-Path $PSScriptRoot "..\CUE4Parse-Natives\ACL\external\acl\includes"
if (-not (Test-Path $aclIncludes)) {
    Write-Warning "ACL submodule is not initialised. Run: git submodule update --init --recursive"
}

dotnet publish CUE4Parse.Cli/CUE4Parse.Cli.csproj `
    -c Release `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

Write-Host "Published to $Output"
Get-ChildItem $Output -Filter "cue4*" | Select-Object Name, Length

# The native library is bundled into the single file, so its absence is invisible from
# the output directory. Ask the binary instead; `info` prints natives even when it exits 3.
$exe = Get-ChildItem $Output -Filter "cue4*" |
    Where-Object { $_.Name -eq "cue4.exe" -or $_.Name -eq "cue4" } |
    Select-Object -First 1

if ($exe) {
    $report = & $exe.FullName info 2>$null | ConvertFrom-Json
    $native = $report.native
    Write-Host "Native capabilities: library=$($native.library) acl=$($native.acl) oodle=$($native.oodle)"

    if (-not $native.acl) {
        $message = "This build cannot decode ACL-compressed animations (library=$($native.library), acl=$($native.acl))."
        if ($AllowMissingNatives) { Write-Warning $message }
        else { throw "$message Fix the native build, or pass -AllowMissingNatives to publish anyway." }
    }
}

# Seed the natives beside the binary so the published folder runs offline. The `info`
# probe above already populated the cache using the tool's own downloader.
if (-not $SkipNativeSidecars) {
    $cache = Join-Path $env:LOCALAPPDATA "cue4\cache"
    $seeded = @()

    if (Test-Path $cache) {
        # Libraries only; mappings.usmap and aes.json are per-user state.
        Get-ChildItem $cache -File |
            Where-Object { $_.Extension -in ".dll", ".so", ".dylib" } |
            ForEach-Object {
                Copy-Item $_.FullName (Join-Path $Output $_.Name) -Force
                $seeded += $_.Name
            }
    }

    if ($seeded.Count -gt 0) { Write-Host "Seeded natives: $($seeded -join ', ')" }
    else { Write-Warning "No natives found in $cache; the published binary will download them on first use." }
}

# `info` without paks exits 3 by design; do not let that become the script's exit code.
exit 0
