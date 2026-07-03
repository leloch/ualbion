# Story-drive helper library. Dot-source from an interactive playthrough script:
#   . F:\Dev\albion\ualbion\_story_drive_lib.ps1
# Provides thin wrappers over the HTTP harness for beat-by-beat story driving.
$script:Base = 'http://localhost:7878'
$script:ShotDir = 'F:\Dev\albion\_shots\story'
if (-not (Test-Path $script:ShotDir)) { New-Item -ItemType Directory -Force $script:ShotDir | Out-Null }

function Start-Albion {
    param([string]$ExtraArgs = '')
    Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $args = @('-d3d','--mute','--harness-http','7878','--trace','F:\Dev\albion\_shots\story\trace.log')
    Start-Process -FilePath 'F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0\UAlbion.exe' `
        -ArgumentList $args -WorkingDirectory 'F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0'
    for ($i=0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 700
        try { if ((Invoke-RestMethod "$Base/healthz" -TimeoutSec 2).ok) { return $true } } catch {}
    }
    return $false
}

function E([string]$body) { # fire a raw event, return the response object
    Invoke-RestMethod -Method POST "$Base/event/raw" -Body $body -ContentType text/plain -TimeoutSec 30
}
function State { Invoke-RestMethod "$Base/state" -TimeoutSec 10 }
function Health { Invoke-RestMethod "$Base/healthz" -TimeoutSec 10 }
function Conv { Invoke-RestMethod "$Base/conversation" -TimeoutSec 10 } # current conversation introspection
function Npcs { Invoke-RestMethod "$Base/npcs" -TimeoutSec 10 }
function Ui { Invoke-RestMethod "$Base/ui" -TimeoutSec 10 }
function Shot([string]$name) {
    Invoke-WebRequest -Method POST "$Base/screenshot" -OutFile (Join-Path $script:ShotDir "$name.png") | Out-Null
    Join-Path $script:ShotDir "$name.png"
}
function ClickId([string]$id, [string]$button = 'Left') {
    Invoke-RestMethod -Method POST "$Base/click" -Body (@{ id = $id; button = $button } | ConvertTo-Json) -ContentType application/json -TimeoutSec 10
}
function Talk([string]$npcSheet) { E "start_dialogue $npcSheet" }
function Respond([int]$n) { E "respond $n" }
function Dismiss { E 'dismiss_message' }
function WaitIdle([int]$seconds = 2) { Start-Sleep -Seconds $seconds }
function LastError { (Invoke-RestMethod "$Base/lasterror" -TimeoutSec 10).detail }
function Scenario([string]$name) { E "synth_scenario $name" }
