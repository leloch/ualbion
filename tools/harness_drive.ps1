# Drive a UAlbion instance through the HTTP harness end-to-end.
# Verifies the API is reachable, can inject events, observe state, enumerate the UI tree,
# and click elements by stable ID.
#
# Usage:
#   pwsh -File _harness_drive.ps1                     # launches UAlbion and runs the smoke
#   pwsh -File _harness_drive.ps1 -ReuseRunning       # assume UAlbion is already up on port 7878
#   pwsh -File _harness_drive.ps1 -Port 7878 -Save 7
#
# Exit code:
#   0 = all checks passed; 1 = something failed (line + reason logged to stderr).

param(
    [int]$Port = 7878,
    [int]$Save = 7,
    [switch]$ReuseRunning,
    [int]$LaunchWaitSeconds = 5
)

$ErrorActionPreference = 'Stop'
$BaseUri = "http://localhost:$Port"
$Failed = $false

function Assert-True($Description, [bool]$Condition) {
    if ($Condition) {
        Write-Host "PASS: $Description" -ForegroundColor Green
    } else {
        Write-Host "FAIL: $Description" -ForegroundColor Red
        $script:Failed = $true
    }
}

function Try-Get($Path) {
    try { return Invoke-RestMethod -Uri "$BaseUri$Path" -Method GET -TimeoutSec 10 }
    catch { return $null }
}

function Try-Post($Path, $Body, [string]$Type = 'application/json') {
    try { return Invoke-RestMethod -Uri "$BaseUri$Path" -Method POST -Body $Body -ContentType $Type -TimeoutSec 10 }
    catch { return $null }
}

# --- 1) Launch UAlbion (unless reusing) ------------------------------------------

$proc = $null
if (-not $ReuseRunning) {
    $exe = Join-Path $PSScriptRoot 'build\UAlbion\bin\Release\net9.0\UAlbion.exe'
    if (-not (Test-Path $exe)) { Write-Error "exe not found at $exe - build it first"; exit 1 }
    $proc = Start-Process -FilePath $exe -ArgumentList '-d3d', '--harness-http', $Port -PassThru -WorkingDirectory (Split-Path $exe)
    Write-Host "Launched UAlbion PID=$($proc.Id); waiting ${LaunchWaitSeconds}s for HTTP to come up..."
    Start-Sleep -Seconds $LaunchWaitSeconds
}

# --- 2) Healthz ------------------------------------------------------------------

$health = Try-Get '/healthz'
Assert-True 'GET /healthz returns object' ($health -ne $null)
Assert-True '/healthz.ok is true' ($health.ok -eq $true)
Assert-True '/healthz.fps > 0' ($health.fps -gt 0)
Assert-True '/healthz.frame > 0' ($health.frame -gt 0)

# --- 3) Load a save --------------------------------------------------------------

$load = Try-Post '/event/raw' "load_game $Save" 'text/plain'
Assert-True "POST /event/raw load_game $Save accepted" ($load -ne $null -and $load.ok -eq $true)

Start-Sleep -Seconds 2

# --- 4) State observation --------------------------------------------------------

$state = Try-Get '/state'
Assert-True 'GET /state returns object' ($state -ne $null)
Assert-True '/state.loaded == true after load' ($state.loaded -eq $true)
Assert-True '/state.map is non-empty' (-not [string]::IsNullOrWhiteSpace($state.map))
Assert-True '/state.party has at least 1 member' ($state.party.Count -ge 1)
Assert-True '/state.leader.id is set' (-not [string]::IsNullOrWhiteSpace($state.leader.id))
Write-Host "  Map: $($state.map), Leader: $($state.leader.id), Party size: $($state.party.Count)"

# --- 5) UI introspection ---------------------------------------------------------

$ui = Try-Get '/ui'
Assert-True 'GET /ui returns object' ($ui -ne $null)
Assert-True '/ui.elements has entries' ($ui.elements.Count -ge 1)
Write-Host "  UI elements: $($ui.elements.Count)"

# Pick an element with a label and a non-trivial bounding box - should always exist (status bar at minimum)
$clickable = $ui.elements | Where-Object { $_.w -gt 0 -and $_.h -gt 0 } | Select-Object -First 1
Assert-True 'at least one element has non-zero size' ($clickable -ne $null)

# --- 6) Click by ID --------------------------------------------------------------

if ($clickable) {
    $clickResp = Try-Post '/click' (ConvertTo-Json @{ id = $clickable.id })
    Assert-True "POST /click {id=$($clickable.id)} returns ok" ($clickResp -ne $null -and $clickResp.ok -eq $true)
}

# --- 7) Bad click returns 404 (surfaced as ok:false in the JSON body) ----------

$badClick = $null
try { $badClick = Invoke-RestMethod -Method POST -Uri "$BaseUri/click" -Body '{"id":"DoesNotExist#0"}' -ContentType 'application/json' }
catch {
    # PS5.1 raises System.Net.WebException (ErrorDetails has the body), PS7 raises
    # Microsoft.PowerShell.Commands.HttpResponseException (Exception.Response.Content).
    # Cover both by chasing whatever's there.
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
        try { $badClick = $_.ErrorDetails.Message | ConvertFrom-Json } catch { }
    }
    if (-not $badClick -and $_.Exception -and $_.Exception.Response) {
        try {
            $r = $_.Exception.Response.GetResponseStream()
            $body = (New-Object IO.StreamReader($r)).ReadToEnd()
            $badClick = $body | ConvertFrom-Json
        } catch { }
    }
}
Assert-True 'invalid click id returns ok:false' ($badClick -ne $null -and $badClick.ok -eq $false)

# --- 8) JSON-structured event ----------------------------------------------------

$jsonEvent = Try-Post '/event' (ConvertTo-Json @{ name = 'set_party_leader'; args = @('Tom', 0) })
# set_party_leader might not exist in all builds - accept either ok:true or a parse error
Assert-True 'POST /event with JSON body returns a response' ($jsonEvent -ne $null)

# --- 9) Stability - check no exception appeared during the run ------------------

Start-Sleep -Seconds 3
$health2 = Try-Get '/healthz'
Assert-True 'service still alive after exercising' ($health2 -ne $null -and $health2.ok -eq $true)
Assert-True 'no lastError accumulated' ([string]::IsNullOrEmpty($health2.lastError))

# --- 10) Quit -------------------------------------------------------------------

$quit = Try-Post '/quit' '' 'application/json'
Assert-True 'POST /quit accepted' ($quit -ne $null -and $quit.ok -eq $true)
Start-Sleep -Seconds 2

if (-not $ReuseRunning -and $proc) {
    # Give the process up to 5s to exit on its own, then force-kill if needed.
    $deadline = (Get-Date).AddSeconds(5)
    while (-not $proc.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $proc.Refresh()
    }
    if (-not $proc.HasExited) {
        Write-Host "Process didn't exit on /quit; force-killing PID $($proc.Id)" -ForegroundColor Yellow
        Stop-Process -Id $proc.Id -Force
    }
}

if ($script:Failed) {
    Write-Host "FAIL: one or more checks did not pass" -ForegroundColor Red
    exit 1
} else {
    Write-Host "PASS: all harness checks succeeded" -ForegroundColor Green
    exit 0
}
