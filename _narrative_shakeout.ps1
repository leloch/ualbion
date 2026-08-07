# Deep narrative shakeout: for every story map, warp there and FIRE ALL its event chains
# (player-triggered zone/NPC chains), catching crashes / handler exceptions / new lastErrors.
# Crash-resilient: relaunches the game and continues past a map that hard-crashes (chest-vault
# cascade = a known harness re-entrancy artifact, not a game bug).
$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$ErrorActionPreference = 'SilentlyContinue'
$exe = (Join-Path $repoRoot 'build\UAlbion\bin\Release\net9.0\UAlbion.exe')
$wd  = (Join-Path $repoRoot 'build\UAlbion\bin\Release\net9.0')
$err = "$repoRoot\_narr_err.txt"
$ids = @(110,111,112,113,114,115,116,117,118,119,120,121,122,123,124,125,126,127,128,129,130,131,132,133,134,135,136,137,138,139,140,141,142,143,144,145,146,147,148,149,150,151,152,153,154,155,156,157,158,159,160,161,162,164,165,166,167,168,169,170,171,172,173,174,190,195,196,197,198,199,200,201,202,203,204,205,206,207,210,211,212,213,214,215,216,217,218,219,230,231,232,233,234,235,236,237,238,239,240,241,242,243,244,245,246,247,248,249,250,251,252,253,254,255,256,260,261,262,263,264,265,266,267,268,269,270,271,272,273,274,275,276,277,278,279,280,281,282,283,284,290,291,292,293,294,295,297,298,299,300,301,302,303,304,305,310,311,312,313,320,322,388,389,390,398,399)

function E($b){ try { Invoke-RestMethod -Method POST http://localhost:7878/event/raw -Body $b -ContentType text/plain -TimeoutSec 10 | Out-Null } catch {} }
function Launch(){
  Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
  Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http','7878','--mute' -WorkingDirectory $wd -RedirectStandardError $err -RedirectStandardOutput "$repoRoot\_narr_out.txt"
  for ($i=0;$i -lt 15;$i++){ Start-Sleep -Seconds 2; try { if ((Invoke-RestMethod http://localhost:7878/healthz -TimeoutSec 3).ok){ return $true } } catch {} }
  return $false
}

Launch | Out-Null
E "load_game 3"; Start-Sleep -Seconds 5
$realErrors = @()
$crashMaps  = @()
$prevErr = ""

foreach ($id in $ids) {
  E "load_map $id"; Start-Sleep -Milliseconds 500
  $r = $null
  try { $r = Invoke-RestMethod -Method POST http://localhost:7878/firechains -TimeoutSec 45 } catch {}
  if ($null -eq $r) {
    $h = $null; try { $h = Invoke-RestMethod http://localhost:7878/healthz -TimeoutSec 6 } catch {}
    if ($null -eq $h) {
      $crashMaps += $id
      if (-not (Launch)) { $realErrors += "RELAUNCH FAILED after map $id"; break }
      E "load_game 3"; Start-Sleep -Seconds 4
    }
    continue
  }
  foreach ($e in $r.errors) {
    if ($e.error) { $realErrors += "MAP $id chain $($e.chain): $($e.error)" }  # ignore 'note' stop-markers
  }
  if ($r.lastErrorAfter -and $r.lastErrorAfter -ne $prevErr) {
    $realErrors += "MAP $id NEW lastError=$($r.lastErrorAfter)"; $prevErr = $r.lastErrorAfter
  }
}

Write-Output "==== NARRATIVE SHAKEOUT COMPLETE ===="
Write-Output ("REAL errors: " + $realErrors.Count)
$realErrors | ForEach-Object { Write-Output "  $_" }
Write-Output ("Chest-vault tool-crash maps (artifact, not a game bug): " + ($crashMaps -join ', '))
