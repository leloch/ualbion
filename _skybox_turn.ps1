# Turn-and-compare test for #54: does the skybox pan in lockstep with the world geometry?
param([int]$Port = 7878, [int]$Save = 3, [string]$OutDir = "F:\Dev\albion\_shots")
$ErrorActionPreference = 'Stop'
$BaseUri = "http://localhost:$Port"
function Post($p,$b,$t='application/json'){ try { Invoke-RestMethod -Uri "$BaseUri$p" -Method POST -Body $b -ContentType $t -TimeoutSec 15 } catch { $null } }
function GetJ($p){ try { Invoke-RestMethod -Uri "$BaseUri$p" -Method GET -TimeoutSec 15 } catch { $null } }
function Shot($name){ try { $c=New-Object System.Net.Http.HttpClient; $c.Timeout=[TimeSpan]::FromSeconds(20); $sc=New-Object System.Net.Http.StringContent('',[Text.Encoding]::UTF8,'application/json'); $r=$c.PostAsync("$BaseUri/screenshot",$sc).Result; $b=$r.Content.ReadAsByteArrayAsync().Result; [IO.File]::WriteAllBytes((Join-Path $OutDir $name),$b); Write-Host "  saved $name"; $c.Dispose() } catch { Write-Host "  shot failed $_" } }

$exe = "F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0\UAlbion.exe"
$proc = Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http',$Port -PassThru -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 6
Post '/event/raw' "load_game $Save" 'text/plain' | Out-Null
Start-Sleep -Seconds 3

$cam = GetJ '/camera'
$fov = [double]$cam.fieldOfView; $aspect = [double]$cam.aspectRatio
$hfov = 2 * [Math]::Atan([Math]::Tan($fov/2) * $aspect)
Write-Host ("vFov={0:F3} aspect={1:F3} -> hFov={2:F3}rad ({3:F1}deg) yawScale=1/hFov={4:F3}" -f $fov,$aspect,$hfov,($hfov*180/[Math]::PI),(1/$hfov))
Write-Host ("start yaw={0:F3}" -f $cam.yaw)
Shot "turn_0.png"

$target = [double]$cam.yaw
foreach ($step in 1..2) {
    $goal = $target + ($step * [Math]::PI/4)  # +45 deg each shot
    $guard = 0
    while ([double](GetJ '/camera').yaw -lt $goal -and $guard -lt 200) {
        Post '/input' (ConvertTo-Json @{ velX=0; velY=0; yaw=-0.05; pitch=0; frames=1 }) | Out-Null
        $guard++
    }
    $y = [double](GetJ '/camera').yaw
    Write-Host ("after step ${step}: yaw={0:F3} (delta={1:F3})" -f $y,($y-$target))
    Shot ("turn_{0}.png" -f ($step*45))
}
Post '/quit' '' | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited){ Stop-Process -Id $proc.Id -Force }
Write-Host "done"
