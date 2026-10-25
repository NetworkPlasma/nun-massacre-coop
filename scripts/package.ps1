$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $workspace 'artifacts'
$version = ([xml](Get-Content -LiteralPath (Join-Path $workspace 'src\NunMassacre.Multiplayer\NunMassacre.Multiplayer.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$release = Join-Path $artifacts 'NunMassacreCoop'
$source = Join-Path $artifacts 'NunMassacreCoop-Source'

# Compress-Archive in Windows PowerShell stores paths with '\', which unzip tools outside Windows take as part of the name.
function Write-Zip([string]$folder, [string]$zip) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($file in Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName) {
            $name = $file.FullName.Substring($folder.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, 'Optimal')
        }
    }
    finally { $archive.Dispose() }
}
# Fresh staging every time, so files removed from the project never linger in an archive.
foreach ($folder in @($release, $source)) { if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force } }
& (Join-Path $PSScriptRoot 'build.ps1') # Stages the plugin under $release\BepInEx\plugins.

# The setup program: one .exe carrying the plugin and the pinned BepInEx loader (checked here and again when it installs).
$plugin = Join-Path $workspace 'src\NunMassacre.Multiplayer\bin\Release\NunMassacre.Multiplayer.dll'
$loader = Join-Path $workspace '.tools\BepInEx_win_x64_5.4.23.3.zip'
$raytracer = Join-Path $workspace 'vendor\vaudio\3d\native\production\windows\vaudionative.dll'
if ((Get-FileHash -LiteralPath $loader -Algorithm SHA256).Hash -ne '41A089E5B1B1F0713B331346BAF6677B1184C69EABEBF51101097954E854C749') { throw 'The pinned BepInEx archive does not match its SHA-256.' }
& dotnet build (Join-Path $workspace 'src\NunMassacreCoop.Setup\NunMassacreCoop.Setup.csproj') -c Release --nologo "-p:Version=$version" "-p:PluginDll=$plugin" "-p:LoaderZip=$loader" "-p:RaytracerDll=$raytracer"
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }
$setup = Join-Path $workspace 'src\NunMassacreCoop.Setup\bin\Release\NunMassacreCoop-Setup.exe'
Copy-Item -LiteralPath $setup -Destination (Join-Path $release 'NunMassacreCoop-Setup.exe') -Force
Copy-Item -LiteralPath $setup -Destination (Join-Path $artifacts "NunMassacreCoop-Setup-$version.exe") -Force

# Player archive: the setup, the plugin on its own (for a game that already has BepInEx 5), the player documentation, the
# third-party notices, and a manifest of every file with its SHA-256.
foreach ($name in @('README.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $workspace $name) -Destination $release -Force }
$manifest = Get-ChildItem -LiteralPath $release -File -Recurse | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.FullName.Substring($release.Length + 1).Replace('\', '/')
}
Set-Content -LiteralPath (Join-Path $release 'MANIFEST.sha256') -Value (@("# Nun Massacre Co-op $version") + $manifest) -Encoding ascii
$playerZip = Join-Path $artifacts "NunMassacreCoop-$version.zip"
Write-Zip $release $playerZip

# Source archive: code, tests, scripts and development documentation (no game files, no builds, no working notes).
$leftOut = '\\(bin|obj)\\|\.rar$'
foreach ($folder in @('src','tests','scripts','docs')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $workspace $folder) -File -Recurse | Where-Object { $_.FullName -notmatch $leftOut }) {
        $relative = $file.FullName.Substring($workspace.Length + 1)
        $destination = Join-Path $source $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}
foreach ($name in @('README.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $workspace $name) -Destination $source -Force }
$sourceZip = Join-Path $artifacts "NunMassacreCoop-Source-$version.zip"
Write-Zip $source $sourceZip
$sums = Get-FileHash -LiteralPath $playerZip, $sourceZip, (Join-Path $artifacts "NunMassacreCoop-Setup-$version.exe") -Algorithm SHA256
Set-Content -LiteralPath (Join-Path $artifacts "SHA256SUMS-$version.txt") -Value ($sums | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $_.Path) }) -Encoding ascii
$sums | Format-Table -AutoSize
