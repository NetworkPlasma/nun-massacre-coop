param([int]$Seconds = 100, [string]$Label = 'coop', [int]$Port = 27845, [switch]$LateJoin)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$gameDirectory = Join-Path $workspace 'Nun Massacre Definitive Edition (Windows) 2022.06.08'
$resultsDirectory = Join-Path $workspace 'test-results'
New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null
$roles = if ($LateJoin) { @('host','client','late') } else { @('host','client') }
if (Get-Process -Name 'Nun Massacre' -ErrorAction SilentlyContinue) { throw 'A previous test run is still running; wait for it to exit.' }
# Old reports would satisfy verify-runtime.ps1 before this run writes its own.
foreach ($role in $roles) { foreach ($suffix in @('.json','.events.log','.log','.model.png')) { Remove-Item -LiteralPath (Join-Path $resultsDirectory "$Label-$role$suffix") -ErrorAction SilentlyContinue } }
foreach ($role in $roles) {
    # Each local process needs its own loader config/log directory. Share immutable game data
    # through NTFS hard links, avoiding both a multi-gigabyte copy and BepInEx config races.
    $instanceDirectory = Join-Path $resultsDirectory "instances\$role"
    New-Item -ItemType Directory -Force -Path $instanceDirectory | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $gameDirectory -File -Recurse) {
        $relative = $file.FullName.Substring($gameDirectory.Length + 1)
        if ($relative -match '^BepInEx\\' -and $relative -notmatch '^BepInEx\\(core|plugins)\\') { continue }
        if ($relative -match '\.(log|txt)$' -or $relative -match '^preloader') { continue }
        $instanceFile = Join-Path $instanceDirectory $relative
        if (!(Test-Path -LiteralPath $instanceFile)) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $instanceFile) | Out-Null
            New-Item -ItemType HardLink -Path $instanceFile -Target $file.FullName | Out-Null
        }
    }
    $reportPath = Join-Path $resultsDirectory "$Label-$role.json"
    $logPath = Join-Path $resultsDirectory "$Label-$role.log"
    # Keep the host alive only as long as the peers so its final report captures
    # the complete roster before clients shut down and disconnect.
    $runSeconds = if ($LateJoin) { $Seconds + 15 } else { $Seconds }
    $connection = if ($role -eq 'host') { '-nmm-host -nmm-test-scene "Nun scene" -nmm-diagnostics' } elseif ($role -eq 'late') { '-nmm-test-latejoin 75' } else { '-nmm-join 127.0.0.1 -nmm-test-actions pickup' }
    $arguments = "-batchmode -screen-fullscreen 0 -screen-width 640 -screen-height 480 -nmm-port $Port -nmm-name $role $connection -nmm-exit-after $runSeconds -nmm-report `"$reportPath`" -logFile `"$logPath`""
    $process = Start-Process -FilePath (Join-Path $instanceDirectory 'Nun Massacre.exe') -WorkingDirectory $instanceDirectory -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.Id | Set-Content -LiteralPath (Join-Path $resultsDirectory "$Label-$role.pid")
    Write-Output "$role process $($process.Id), reports in $reportPath"
    if ($role -eq 'host') { Start-Sleep -Seconds 3 }
}
