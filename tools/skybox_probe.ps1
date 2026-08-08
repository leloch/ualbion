# Screenshot every 3D save to find one with a visible skybox; confirms #54 renders coherently.
param([int]$Port = 7878, [string]$OutDir)
$repoRoot = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { (Get-Location).Path }
if (-not $OutDir) { $OutDir = Join-Path $repoRoot '_shots' }
$ErrorActionPreference = 'Stop'
$BaseUri = "http://localhost:$Port"
New-Item -ItemType Directory -Force $OutDir | Out-Null
function Post($p,$b,$t='application/json'){ try { Invoke-RestMethod -Uri "$BaseUri$p" -Method POST -Body $b -ContentType $t -TimeoutSec 15 } catch { $null } }
function GetJ($p){ try { Invoke-RestMethod -Uri "$BaseUri$p" -Method GET -TimeoutSec 15 } catch { $null } }
function Shot($name){ try { $c=New-Object System.Net.Http.HttpClient; $c.Timeout=[TimeSpan]::FromSeconds(20); $sc=New-Object System.Net.Http.StringContent('',[Text.Encoding]::UTF8,'application/json'); $r=$c.PostAsync("$BaseUri/screenshot",$sc).Result; $b=$r.Content.ReadAsByteArrayAsync().Result; [IO.File]::WriteAllBytes((Join-Path $OutDir $name),$b); Write-Host "  saved $name ($($b.Length)b)"; $c.Dispose() } catch { Write-Host "  shot failed $_" } }

$exe = "$repoRoot\build\UAlbion\bin\Release\net9.0\UAlbion.exe"
$proc = Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http',$Port -PassThru -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 6

foreach ($sv in 1..13) {
    Post '/event/raw' "load_game $sv" 'text/plain' | Out-Null
    Start-Sleep -Seconds 2
    $lab = GetJ '/labyrinth'
    $mt = "$($lab.mapType)"
    Write-Host "save $sv : map=$($lab.mapId) type=$mt"
    if ($mt -match '3|Three') { Shot "map3d_${sv}.png" }
}
Post '/quit' '' | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited){ Stop-Process -Id $proc.Id -Force }
Write-Host "done"
