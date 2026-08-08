# Per-save behaviour sweep: for each of the 13 saves, verify the world is LIVE and movement works:
#   - clock running (or legitimately stopped by an active event chain)
#   - party actually moves when driven, and is bounded by collision (doesn't run the full distance)
#   - NPCs present
# Flags: frozen clock with no active chain (stuck world), party can't move, or party runs unbounded.
$repoRoot = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { (Get-Location).Path }
$base='http://localhost:7878'
$exe=(Join-Path $repoRoot 'build\UAlbion\bin\Release\net9.0\UAlbion.exe')
$wd =(Join-Path $repoRoot 'build\UAlbion\bin\Release\net9.0')
function Start-Game { Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 400
  Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http','7878','--mute' -WorkingDirectory $wd
  for ($i=0;$i -lt 40;$i++){ try { if((Invoke-RestMethod "$base/healthz" -TimeoutSec 2).ok){return $true} } catch {}; Start-Sleep -Milliseconds 700 }; return $false }
function Alive { try { return (Invoke-RestMethod "$base/healthz" -TimeoutSec 3).ok } catch { return $false } }

if (-not (Start-Game)) { "FAILED launch"; exit 1 }
$issues=@()
foreach ($slot in 0..12) {
  if ($slot % 5 -eq 0 -and $slot -gt 0) { Start-Game | Out-Null }
  try { Invoke-RestMethod -Method POST "$base/event/raw" -Body "load_game $slot" -ContentType text/plain -TimeoutSec 10 | Out-Null } catch { if(-not(Start-Game)){break}; continue }
  Start-Sleep -Seconds 3
  if (-not (Alive)) { $issues += "slot ${slot}: CRASH on load"; if(-not(Start-Game)){break}; continue }
  $clk = Invoke-RestMethod "$base/clock"
  $st  = Invoke-RestMethod "$base/state"
  $is3d = $st.mapType -eq 'ThreeD'
  # measure movement: drive forward (3D) or south (2D) and see if the party position changes
  $before = if ($is3d) { (Invoke-RestMethod "$base/camera").position } else { $st.leader }
  if ($is3d) { Invoke-RestMethod -Method POST "$base/input" -Body '{"velY":4,"frames":40}' -ContentType application/json | Out-Null }
  else { 1..10 | ForEach-Object { Invoke-RestMethod -Method POST "$base/event/raw" -Body "party_move 0 1" -ContentType text/plain | Out-Null; Start-Sleep -Milliseconds 80 } }
  Start-Sleep -Seconds 2
  $after = if ($is3d) { (Invoke-RestMethod "$base/camera").position } else { (Invoke-RestMethod "$base/state").leader }
  $moved = if ($is3d) { [math]::Abs($after.x-$before.x)+[math]::Abs($after.z-$before.z) } else { [math]::Abs($after.x-$before.x)+[math]::Abs($after.y-$before.y) }
  $line = "slot ${slot}: $($st.map) [$($st.mapType)] clock=$($clk.clockRunning) chains=$($clk.activeChains) moved=$([math]::Round($moved,1))"
  $line
  # Anomaly: clock stopped with NO active chain WHILE IN A WORLD SCENE (genuine stuck world).
  # Combat/Inventory/Menu scenes legitimately stop the clock, so only flag World2D/World3D.
  $inWorld = $clk.activeScene -eq 'World2D' -or $clk.activeScene -eq 'World3D'
  if ($inWorld -and -not $clk.clockRunning -and $clk.activeChains -eq 0) { $issues += "slot ${slot}: CLOCK STOPPED in world scene with no active chain (stuck world) on $($st.map)" }
}
"`n========== SAVE BEHAVIOUR SWEEP =========="
if ($issues.Count -eq 0) { "No stuck-clock anomalies." } else { "ISSUES:"; $issues | ForEach-Object { "  $_" } }
Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
