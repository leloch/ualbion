# Sweep EVERY map: new_game into it, and on 3D maps run /collisionscan, flagging any map with
# see-through walls or see-through objects (solid-looking geometry the 0x78 block mask misses =
# walk-through-wall data bug) or a blockMismatch. Relaunches periodically to stay fresh.
$base='http://localhost:7878'
$exe='F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0\UAlbion.exe'
$wd ='F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0'

function Start-Game {
    Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http','7878','--mute' -WorkingDirectory $wd
    for ($i=0; $i -lt 40; $i++) { try { if ((Invoke-RestMethod "$base/healthz" -TimeoutSec 2).ok) { return $true } } catch {}; Start-Sleep -Milliseconds 700 }
    return $false
}
function Alive { try { return (Invoke-RestMethod "$base/healthz" -TimeoutSec 3).ok } catch { return $false } }

# Map names (id-bearing enum members) from Base/Map.cs
$maps = Select-String -Path 'F:\Dev\albion\ualbion\src\Base\Map.cs' -Pattern '^\s+([A-Za-z0-9_]+)\s*=\s*\d+' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Where-Object { $_ -notmatch '^Unk\d' }

if (-not (Start-Game)) { "FAILED launch"; exit 1 }

$threeD=0; $issues=@(); $scanned=0; $n=0
foreach ($m in $maps) {
    $n++
    if ($n % 8 -eq 0) { Start-Game | Out-Null }   # periodic relaunch for freshness
    try { Invoke-RestMethod -Method POST "$base/event/raw" -Body "new_game $m 1 1" -ContentType text/plain -TimeoutSec 10 | Out-Null }
    catch { if (-not (Start-Game)) { break }; continue }
    Start-Sleep -Seconds 2
    if (-not (Alive)) { $issues += "[$m] CRASH on load"; if (-not (Start-Game)) { break }; continue }
    try { $cs = Invoke-RestMethod "$base/collisionscan" -TimeoutSec 8 } catch { continue }
    if (-not $cs.is3d) { continue }
    $threeD++; $scanned++
    if ($cs.seeThruWalls -gt 0 -or $cs.seeThruObjects -gt 0 -or $cs.blockMismatch -gt 0) {
        $issues += "[$m] seeThruWalls=$($cs.seeThruWalls) seeThruObjects=$($cs.seeThruObjects) blockMismatch=$($cs.blockMismatch) wallHisto=$($cs.wallCollisionHisto | ConvertTo-Json -Compress) objHisto=$($cs.objCollisionHisto | ConvertTo-Json -Compress)"
        if ($cs.seeThruSamples) { $issues += "    wallSamples: $($cs.seeThruSamples -join '; ')" }
        if ($cs.seeThruObjSamples) { $issues += "    objSamples: $($cs.seeThruObjSamples -join '; ')" }
    }
}
"`n========== ALL-MAPS COLLISION SWEEP =========="
"3D maps scanned: $scanned"
if ($issues.Count -eq 0) { "NO COLLISION-DATA ISSUES on any 3D map." } else { "ISSUES ($($issues.Count)):"; $issues | ForEach-Object { "  $_" } }
Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
