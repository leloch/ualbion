# Drive UAlbion through every save and exercise post-load behaviour:
# - Load
# - Advance game time multiple hours
# - Trigger encounter
# - Run combat rounds with various actions
# - Capture lastError if any
# Surfaces bugs that --startuponly smoke can't catch.
#
# Usage:
#   powershell -File _harness_explore.ps1
#   powershell -File _harness_explore.ps1 -Save 7 -Verbose
#
# Output: writes _explore_report.tsv with one row per save+phase + any errors.

param(
    [int]$Port = 7878,
    [int[]]$Saves = @(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 100),
    [int]$LaunchWaitSeconds = 5,
    [switch]$Verbose
)

$ErrorActionPreference = 'Stop'
$BaseUri = "http://localhost:$Port"
$reportPath = Join-Path $PSScriptRoot '_explore_report.tsv'
$report = @("save`tphase`tok`tdetail")

function Try-Get($Path) {
    try { return Invoke-RestMethod -Uri "$BaseUri$Path" -Method GET -TimeoutSec 10 }
    catch { return $null }
}

function Try-Post($Path, $Body) {
    try { return Invoke-RestMethod -Uri "$BaseUri$Path" -Method POST -Body $Body -ContentType 'text/plain' -TimeoutSec 10 }
    catch { return @{ ok=$false; error="$_" } }
}

function Run-Save($save) {
    Write-Host "`n========== Save $save ==========" -ForegroundColor Cyan

    # 1. Load
    $resp = Try-Post '/event/raw' "load_game $save"
    if (-not $resp.ok) {
        $script:report += "$save`tload`tFALSE`t$($resp.error)"
        return
    }
    Start-Sleep -Seconds 2

    $state = Try-Get '/state'
    if (-not $state.loaded) {
        $script:report += "$save`tload`tFALSE`tstate.loaded=false after load"
        return
    }
    $script:report += "$save`tload`tTRUE`tmap=$($state.map) party=$($state.party.Count) tick=$($state.tickCount)"
    Write-Host "  loaded: $($state.map), party=$($state.party.Count)"

    # 2. Advance time 24h to fire HourElapsed many times (tests StatusConditionTicker)
    $hours = Try-Post '/event/raw' 'modify_hours AddAmount 24'
    Start-Sleep -Seconds 3
    $hp = Try-Get '/healthz'
    if ($hp.lastError) {
        $script:report += "$save`ttime24h`tFALSE`tlastError=$($hp.lastError)"
        Write-Host "  TIME-ADVANCE ERROR: $($hp.lastError)" -ForegroundColor Red
    } else {
        $script:report += "$save`ttime24h`tTRUE`tcmds=$($hp.commandsProcessed)"
    }

    # 3. Encounter and combat (only for maps where this makes sense)
    if ($state.map -match 'HunterClanCellar|Drinno|Jirinaar|Nakiridaani|SnirdArmoury|Winion|TownHall') {
        $enc = Try-Post '/event/raw' 'encounter MonsterGroup.OneArgim'
        Start-Sleep -Seconds 3
        $hp = Try-Get '/healthz'
        if ($hp.lastError) {
            $script:report += "$save`tencounter`tFALSE`t$($hp.lastError)"
            Write-Host "  ENCOUNTER ERROR: $($hp.lastError)" -ForegroundColor Red
        } else {
            $script:report += "$save`tencounter`tTRUE`t"

            # Try 3 combat rounds — first member of party attacks
            $leader = $state.leader.id -replace 'PartyMember\.', 'PartySheet.'
            for ($r = 1; $r -le 3; $r++) {
                Try-Post '/event/raw' "queue_combat_action $leader Melee -1" | Out-Null
                Try-Post '/event/raw' 'begin_combat_round' | Out-Null
                Start-Sleep -Seconds 2
                $hp = Try-Get '/healthz'
                if ($hp.lastError) {
                    $script:report += "$save`tround$r`tFALSE`t$($hp.lastError)"
                    Write-Host "  ROUND $r ERROR: $($hp.lastError)" -ForegroundColor Red
                    break
                }
            }
            if (-not $hp.lastError) {
                $script:report += "$save`tcombat`tTRUE`t3 rounds clean"
            }
        }
    }

    # 4. Final state
    $finalState = Try-Get '/state'
    if ($finalState) {
        $script:report += "$save`tfinal`tTRUE`thp=$($finalState.party[0].hp)/$($finalState.party[0].hpMax) cmds=$($finalState.commandsProcessed)"
    }
}

# Launch UAlbion if not already up
$exe = Join-Path $PSScriptRoot 'build\UAlbion\bin\Release\net9.0\UAlbion.exe'
$proc = Start-Process -FilePath $exe -ArgumentList '-d3d', '--harness-http', $Port, '--mute' -PassThru -WorkingDirectory (Split-Path $exe)
Write-Host "Launched UAlbion PID=$($proc.Id)"
Start-Sleep -Seconds $LaunchWaitSeconds

try {
    foreach ($save in $Saves) {
        Run-Save $save
    }
} finally {
    Write-Host "`nWriting report to $reportPath"
    $report | Set-Content $reportPath -Encoding UTF8

    Write-Host "`nShutting down..."
    try { Invoke-RestMethod -Method POST -Uri "$BaseUri/quit" -Body '' -ContentType 'application/json' -TimeoutSec 5 | Out-Null } catch { }
    Start-Sleep -Seconds 3
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}

Write-Host "`nReport:"
Get-Content $reportPath | ForEach-Object { Write-Host $_ }
