# Screenshot-verify #46 (underlined headers) + #27 (double-thick button frames) on the inventory.
param([int]$Port = 7878, [int]$Save = 7, [string]$OutDir)
$repoRoot = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { (Get-Location).Path }
if (-not $OutDir) { $OutDir = Join-Path $repoRoot '_shots' }
$ErrorActionPreference = 'Stop'
$BaseUri = "http://localhost:$Port"
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Post($Path, $Body, $Type = 'application/json') {
    try { return Invoke-RestMethod -Uri "$BaseUri$Path" -Method POST -Body $Body -ContentType $Type -TimeoutSec 15 }
    catch { Write-Host "POST $Path failed: $_" -ForegroundColor Yellow; return $null }
}
function Get-J($Path) { try { return Invoke-RestMethod -Uri "$BaseUri$Path" -Method GET -TimeoutSec 15 } catch { return $null } }
function Shot($name) {
    $out = Join-Path $OutDir $name
    try {
        $client = New-Object System.Net.Http.HttpClient
        $client.Timeout = [TimeSpan]::FromSeconds(20)
        $content = New-Object System.Net.Http.StringContent('', [Text.Encoding]::UTF8, 'application/json')
        $resp = $client.PostAsync("$BaseUri/screenshot", $content).Result
        $bytes = $resp.Content.ReadAsByteArrayAsync().Result
        [IO.File]::WriteAllBytes($out, $bytes)
        Write-Host "saved $out ($($bytes.Length) bytes)"
        $client.Dispose()
    } catch { Write-Host "shot $name failed: $_" -ForegroundColor Yellow }
}

$exe = "$repoRoot\build\UAlbion\bin\Release\net9.0\UAlbion.exe"
$proc = Start-Process -FilePath $exe -ArgumentList '-d3d', '--harness-http', $Port -PassThru -WorkingDirectory (Split-Path $exe)
Write-Host "Launched PID=$($proc.Id)"
Start-Sleep -Seconds 6

$h = Get-J '/healthz'; Write-Host "health ok=$($h.ok) fps=$($h.fps)"
Post '/event/raw' "load_game $Save" 'text/plain' | Out-Null
Start-Sleep -Seconds 3
$s = Get-J '/state'; Write-Host "map=$($s.map) leader=$($s.leader.id)"

# Open inventory for the leader, then go to the Stats page (page II) which has Attributes/Skills headers + I/II/III tabs.
Post '/event/raw' "inv:open $($s.leader.id)" 'text/plain' | Out-Null
Start-Sleep -Seconds 2
Shot 'inv_summary.png'
Post '/event/raw' 'inv:set_page Stats' 'text/plain' | Out-Null
Start-Sleep -Seconds 2
Shot 'inv_stats.png'
Post '/event/raw' 'inv:set_page Misc' 'text/plain' | Out-Null
Start-Sleep -Seconds 2
Shot 'inv_misc.png'

Post '/quit' '' 'application/json' | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Write-Host "done"
