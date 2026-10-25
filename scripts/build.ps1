param([switch]$Install)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$gameDirectory = Join-Path $workspace 'Nun Massacre Definitive Edition (Windows) 2022.06.08'
$project = Join-Path $workspace 'src\NunMassacre.Multiplayer\NunMassacre.Multiplayer.csproj'
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$outputDirectory = Join-Path $workspace 'artifacts\NunMassacreCoop'
New-Item -ItemType Directory -Force -Path (Join-Path $outputDirectory 'BepInEx\plugins\NunMassacreCoop') | Out-Null
Copy-Item -LiteralPath (Join-Path $workspace 'src\NunMassacre.Multiplayer\bin\Release\NunMassacre.Multiplayer.dll') -Destination (Join-Path $outputDirectory 'BepInEx\plugins\NunMassacreCoop') -Force
# Vercidium Audio's raytracer (its SDK unpacked under vendor\vaudio; see README): beside the plugin.
$raytracer = Join-Path $workspace 'vendor\vaudio\3d\native\production\windows\vaudionative.dll'
if (!(Test-Path -LiteralPath $raytracer)) { throw "Vercidium Audio's vaudionative.dll is missing: $raytracer" }
Copy-Item -LiteralPath $raytracer -Destination (Join-Path $outputDirectory 'BepInEx\plugins\NunMassacreCoop') -Force
if ($Install) {
    $loader = Join-Path $workspace '.tools\BepInEx'
    if (!(Test-Path -LiteralPath (Join-Path $gameDirectory 'BepInEx\core\BepInEx.dll'))) {
        Copy-Item -LiteralPath (Join-Path $loader 'BepInEx') -Destination $gameDirectory -Recurse -Force
        foreach ($name in @('winhttp.dll','doorstop_config.ini')) {
            $target = Join-Path $gameDirectory $name
            if (Test-Path -LiteralPath $target) { throw "Existing loader file needs review before installation: $target" }
            Copy-Item -LiteralPath (Join-Path $loader $name) -Destination $gameDirectory
        }
    }
    $pluginDirectory = Join-Path $gameDirectory 'BepInEx\plugins\NunMassacreCoop'
    New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $workspace 'src\NunMassacre.Multiplayer\bin\Release\NunMassacre.Multiplayer.dll') -Destination $pluginDirectory -Force
    Copy-Item -LiteralPath $raytracer -Destination $pluginDirectory -Force
    Write-Output "Installed co-op mod in $pluginDirectory"
}
