# Verify #43 minimap: off (default/vanilla) vs on, on a 3D map.
param([int]$Port = 7878, [int]$Save = 7, [string]$OutDir)
$repoRoot = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { (Get-Location).Path }
if (-not $OutDir) { $OutDir = Join-Path $repoRoot '_shots' }
$ErrorActionPreference = 'Stop'
$BaseUri = "http://localhost:$Port"
function Post($p,$b,$t='application/json'){ try { Invoke-RestMethod -Uri "$BaseUri$p" -Method POST -Body $b -ContentType $t -TimeoutSec 15 } catch { $null } }
function Shot($name){ try { $c=New-Object System.Net.Http.HttpClient; $c.Timeout=[TimeSpan]::FromSeconds(20); $sc=New-Object System.Net.Http.StringContent('',[Text.Encoding]::UTF8,'application/json'); $r=$c.PostAsync("$BaseUri/screenshot",$sc).Result; $b=$r.Content.ReadAsByteArrayAsync().Result; [IO.File]::WriteAllBytes((Join-Path $OutDir $name),$b); Write-Host "  saved $name"; $c.Dispose() } catch { Write-Host "  shot failed $_" } }

$exe = "$repoRoot\build\UAlbion\bin\Release\net9.0\UAlbion.exe"
$proc = Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http',$Port -PassThru -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 6

# Default (vanilla): minimap off
Post '/event/raw' "load_game $Save" 'text/plain' | Out-Null
Start-Sleep -Seconds 3
Shot 'minimap_off.png'

# Enable, reload so AutomapDialog.Subscribed re-runs and shows it
Post '/event/raw' 'set Game.Graphics.Minimap true' 'text/plain' | Out-Null
Start-Sleep -Milliseconds 500
Post '/event/raw' "load_game $Save" 'text/plain' | Out-Null
Start-Sleep -Seconds 3
# Take a step so MarkDiscovered fires and refreshes the minimap too
Post '/input' (ConvertTo-Json @{ velX=0; velY=1; yaw=0; pitch=0; frames=20 }) | Out-Null
Start-Sleep -Milliseconds 800
Shot 'minimap_on.png'

Post '/quit' '' | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited){ Stop-Process -Id $proc.Id -Force }
Write-Host "done"
