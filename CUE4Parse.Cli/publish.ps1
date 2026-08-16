#!/usr/bin/env pwsh
# Publishes cue4.exe as a self-contained single file.
# Trimming and NativeAOT are deliberately NOT used: ObjectTypeRegistry reflects
# over the assembly at static init, and trimming breaks UObject construction.

param(
    [string] $Runtime = "win-x64",
    [string] $Output  = "./artifacts"
)

$ErrorActionPreference = "Stop"

dotnet publish CUE4Parse.Cli/CUE4Parse.Cli.csproj `
    -c Release `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

Write-Host "Published to $Output"
Get-ChildItem $Output -Filter "cue4*" | Select-Object Name, Length
