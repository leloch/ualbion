# Verify #39 distance fog: off (vanilla) vs on, on a 3D map.
param([int]$Port = 7878, [int]$Save = 3, [string]$OutDir)
$repoRoot = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { (Get-Location).Path }
if (-not $OutDir) { $OutDir = Join-Path $repoRoot '_shots' }
$ErrorActionPreference = 'Stop'
$BaseUri = "http://localhost:$Port"
function Post($p,$b,$t='application/json'){ try { Invoke-RestMethod -Uri "$BaseUri$p" -Method POST -Body $b -ContentType $t -TimeoutSec 15 } catch { Write-Host "POST $p err $_"; $null } }
function GetJ($p){ try { Invoke-RestMethod -Uri "$BaseUri$p" -Method GET -TimeoutSec 15 } catch { $null } }
function Shot($name){ try { $c=New-Object System.Net.Http.HttpClient; $c.Timeout=[TimeSpan]::FromSeconds(20); $sc=New-Object System.Net.Http.StringContent('',[Text.Encoding]::UTF8,'application/json'); $r=$c.PostAsync("$BaseUri/screenshot",$sc).Result; $b=$r.Content.ReadAsByteArrayAsync().Result; [IO.File]::WriteAllBytes((Join-Path $OutDir $name),$b); Write-Host "  saved $name"; $c.Dispose() } catch { Write-Host "  shot failed $_" } }

$exe = "$repoRoot\build\UAlbion\bin\Release\net9.0\UAlbion.exe"
$proc = Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http',$Port -PassThru -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 6

# Fog OFF (default vanilla)
$r=Post '/event/raw' "load_game $Save" 'text/plain'; Write-Host "load ok=$($r.ok) err=$($r.error)"
Start-Sleep -Seconds 3
$h=GetJ '/healthz'; Write-Host "off: ok=$($h.ok) lastErr=$($h.lastError)"
Shot 'fog_off.png'

# Fog ON
Post '/event/raw' 'set Game.Graphics.DungeonFog true' 'text/plain' | Out-Null
Start-Sleep -Milliseconds 600
Shot 'fog_on.png'
$h=GetJ '/healthz'; Write-Host "on: ok=$($h.ok) lastErr=$($h.lastError)"

Post '/quit' '' | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited){ Stop-Process -Id $proc.Id -Force }
Write-Host "done"
