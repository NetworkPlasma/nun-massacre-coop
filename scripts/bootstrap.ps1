$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $workspace '.tools'
$archive = Join-Path $toolsDirectory 'BepInEx_win_x64_5.4.23.3.zip'
$expected = '41A089E5B1B1F0713B331346BAF6677B1184C69EABEBF51101097954E854C749'
New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'BepInEx archive integrity check failed.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolsDirectory 'BepInEx') -Force
Write-Output 'Pinned BepInEx build dependencies are ready.'
