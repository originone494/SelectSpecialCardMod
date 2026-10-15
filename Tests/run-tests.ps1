param([switch]$Headless)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& dotnet build (Join-Path $projectRoot 'SelectSpecialCardMod.csproj') --ignore-failed-sources -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'Architecture.Tests/Architecture.Tests.csproj') -- --patches
if ($LASTEXITCODE -ne 0) { throw 'Architecture regression failed.' }
if ($Headless) {
    $probeRoot = Join-Path $PSScriptRoot 'GodotProbe'
    & dotnet build (Join-Path $probeRoot 'Probe.csproj') --ignore-failed-sources -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Godot probe build failed.' }
    $godotBinary = (& dotnet msbuild (Join-Path $projectRoot 'SelectSpecialCardMod.csproj') -getProperty:GodotExe).Trim()
    $previousRollForward = $env:DOTNET_ROLL_FORWARD
    try {
        $env:DOTNET_ROLL_FORWARD = 'Major'
        & $godotBinary --headless --path $probeRoot --editor --import --quit
        if ($LASTEXITCODE -ne 0) { throw 'Godot probe import failed.' }
        & $godotBinary --headless --path $probeRoot
        if ($LASTEXITCODE -ne 0) { throw 'Godot probe failed.' }
    } finally { $env:DOTNET_ROLL_FORWARD = $previousRollForward }
}
