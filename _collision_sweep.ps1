# Cross-save 3D collision-consistency sweep.
# Loads each of the 13 saves (crash-free load_game path), and on every 3D map runs
# /collisionscan, asserting blockMismatch==0 and recording water/wall/open counts.
$base = 'http://localhost:7878'
$exe  = 'F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0\UAlbion.exe'
$wd   = 'F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0'

function Start-Game {
    Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http','7878','--mute' -WorkingDirectory $wd
    for ($i=0; $i -lt 40; $i++) {
        try { $h = Invoke-RestMethod "$base/healthz" -TimeoutSec 2; if ($h.ok) { return $true } } catch {}
        Start-Sleep -Milliseconds 700
    }
    return $false
}

if (-not (Start-Game)) { "FAILED to launch"; exit 1 }

$results = @()
foreach ($slot in 0..12) {
    try {
        Invoke-RestMethod -Method POST "$base/event/raw" -Body "load_game $slot" -ContentType text/plain -TimeoutSec 10 | Out-Null
    } catch {
        "slot $slot : LOAD CRASHED - relaunching"
        if (-not (Start-Game)) { "FAILED to relaunch"; break }
        continue
    }
    Start-Sleep -Seconds 3
    try {
        $st = Invoke-RestMethod "$base/state" -TimeoutSec 5
    } catch { "slot $slot : state failed"; continue }
    if ($st.mapType -ne 'ThreeD') { "slot $slot : $($st.map) [$($st.mapType)] skip-2D"; continue }
    try {
        $cs = Invoke-RestMethod "$base/collisionscan" -TimeoutSec 10
        $flag = if ($cs.blockMismatch -ne 0) { '  <<< MISMATCH' } else { '' }
        "slot $slot : $($cs.map)  w=$($cs.w) h=$($cs.h)  wall=$($cs.wallTiles) water=$($cs.waterTiles) obj=$($cs.objectBlocked) open=$($cs.openTiles)  mismatch=$($cs.blockMismatch)$flag"
        $results += $cs
    } catch { "slot $slot : collisionscan failed" }
}
$mm = @($results | Where-Object { $_.blockMismatch -ne 0 }).Count
"=== DONE: $($results.Count) 3D maps scanned, $mm with mismatch ==="
