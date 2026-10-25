param([string]$Label = 'coop', [switch]$LateJoin, [int]$TimeoutSeconds = 150)
$ErrorActionPreference = 'Stop'
$results = Join-Path (Split-Path -Parent $PSScriptRoot) 'test-results'
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$roles = if ($LateJoin) { @('host','client','late') } else { @('host','client') }
do {
    $reports = @{}
    foreach ($role in $roles) {
        $path = Join-Path $results "$Label-$role.json"
        try { if (Test-Path -LiteralPath $path) { $reports[$role] = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } } catch { }
    }
    $clientEvents = Join-Path $results "$Label-client.events.log"
    $actionsPassed = (Test-Path -LiteralPath $clientEvents) -and ((Get-Content -LiteralPath $clientEvents -Raw) -match 'TEST cabinet native state PASS')
    $allReady = $reports.Count -eq $roles.Count
    foreach ($role in $roles) {
        if (!$reports.ContainsKey($role) -or !$reports[$role].active -or !$reports[$role].ready -or $reports[$role].scene -ne 'Nun scene' -or $reports[$role].remoteCount -ne ($roles.Count - 1)) { $allReady = $false }
    }
    if ($allReady -and $actionsPassed) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTime]::UtcNow -lt $deadline)
if (!$allReady -or !$actionsPassed) { throw 'Runtime validation timed out: all peers and successful actions were not observed together.' }
$checks = [System.Collections.Generic.List[string]]::new()
$checks.Add('All game processes authenticated, loaded Nun scene, and saw every other player.')
$log = Get-Content -LiteralPath $clientEvents -Raw
foreach ($check in @('pickup native inventory','drop native inventory','cabinet native state')) {
    if ($log -notmatch ('TEST ' + $check + ' PASS')) { throw "Native action failed: $check" }
    $checks.Add("Client $check passed.")
}
$open = @($reports['host'].objects | Where-Object open)
if ($open.Count -lt 1) { throw 'Host has no open cabinet.' }
foreach ($role in $roles | Where-Object { $_ -ne 'host' }) {
    foreach ($expected in $open) {
        $actual = @($reports[$role].objects | Where-Object id -eq $expected.id)
        if ($actual.Count -ne 1 -or !$actual[0].open) { throw "$role did not receive the open cabinet state." }
        foreach ($axis in @('x','y','z')) {
            if ([Math]::Abs([double]$expected.position.$axis - [double]$actual[0].position.$axis) -gt .02) { throw "$role cabinet position differs from the host." }
        }
    }
    $checks.Add("$role received the host cabinet state and position within 2 cm.")
}
foreach ($role in $roles) {
    $events = Get-Content -LiteralPath (Join-Path $results "$Label-$role.events.log") -Raw
    if ($events -match 'Session error:|Action .* failed:|Cannot apply |Cannot capture |Rejected packet') { throw "$role logged a multiplayer runtime error." }
    if ($events -notmatch 'Loaded character ') { throw "$role did not load the supplied model." }
    Copy-Item -LiteralPath (Join-Path $results "$Label-$role.json") -Destination (Join-Path $results "$Label-$role.verified.json") -Force
}
$checks.Add('Every peer loaded the supplied character mesh and texture without a model fallback.')
$checks.Add('No mod session/action/snapshot errors in the captured run.')
$summary = [PSCustomObject]@{ Result = 'PASS'; UtcTime = [DateTime]::UtcNow.ToString('o'); Peers = $roles.Count; Checks = $checks; Scope = 'Automated native inventory/cabinet, host/client/late-join test; not a complete gameplay or combat test.' }
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $results "$Label-validation.json")
$summary | ConvertTo-Json -Depth 5
