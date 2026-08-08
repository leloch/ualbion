# Conversation correctness shakeout.
# Warps to each chapter (synth_scenario), talks to every real (NpcSheet) NPC on the map, advances
# past the greeting, exercises the profession option, and FLAGS: failed-to-open dialogues, empty or
# unresolved-token text (a literal '{' = an unrendered formatter token, e.g. the old {NAME} bug),
# missing options, and any engine error logged during the exchange. Deeper than the chain shakeout:
# it drives the real dialogue UI + option selection, not just chain firing.
$repoRoot = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { (Get-Location).Path }
$base = 'http://localhost:7878'
$exe  = (Join-Path $repoRoot 'build\UAlbion\bin\Release\net9.0\UAlbion.exe')
$wd   = (Join-Path $repoRoot 'build\UAlbion\bin\Release\net9.0')

function Start-Game {
    Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Start-Process -FilePath $exe -ArgumentList '-d3d','--harness-http','7878','--mute' -WorkingDirectory $wd
    for ($i=0; $i -lt 40; $i++) {
        try { if ((Invoke-RestMethod "$base/healthz" -TimeoutSec 2).ok) { return $true } } catch {}
        Start-Sleep -Milliseconds 700
    }
    return $false
}
function Alive { try { return (Invoke-RestMethod "$base/healthz" -TimeoutSec 3).ok } catch { return $false } }
function Post($body) { try { Invoke-RestMethod -Method POST "$base/event/raw" -Body $body -ContentType text/plain -TimeoutSec 8 | Out-Null } catch {} }

$scenarios = 'intro','nakiridaani','jirinaar','hunterclan','drinno','srimalinar','beloveno','kounos','maini','djicantos','kenget','finale'
$issues = @()
$talked = 0

foreach ($sc in $scenarios) {
    # Relaunch fresh per chapter: synth_scenario warps accumulate state that breaks conversation
    # greetings after ~5 warps in one process (a cockpit-tool artifact; real save-loading is fine),
    # so a clean process per chapter gives an accurate per-chapter signal.
    if (-not (Start-Game)) { $issues += "[$sc] FAILED to launch"; break }
    Post "synth_scenario $sc"
    Start-Sleep -Seconds 3
    if (-not (Alive)) { $issues += "[$sc] CRASH warping in"; if (-not (Start-Game)) { break }; continue }

    try { $npcs = Invoke-RestMethod "$base/npcs" -TimeoutSec 5 } catch { $issues += "[$sc] /npcs failed"; continue }
    $talkable = @($npcs.npcs | Where-Object { $_.id -like 'NpcSheet.*' } | Select-Object -ExpandProperty id -Unique)
    "[$sc] map NPCs: $($npcs.npcs.Count), talkable(NpcSheet): $($talkable.Count) -> $($talkable -join ', ')"

    foreach ($npc in $talkable) {
        # Ensure no leftover conversation from the previous NPC (start while one is closing => bad state).
        Post "end_dialogue"
        for ($w=0; $w -lt 12; $w++) { $cc = try { Invoke-RestMethod "$base/conversation" -TimeoutSec 5 } catch { $null }; if (-not $cc -or -not $cc.active) { break }; Start-Sleep -Milliseconds 200 }
        Invoke-RestMethod "$base/log?clear=1&n=1" -TimeoutSec 5 | Out-Null
        Post "start_dialogue $npc"
        # Poll until the greeting has actually loaded (active + non-empty text), up to ~2.4s.
        $c = $null
        for ($w=0; $w -lt 12; $w++) {
            Start-Sleep -Milliseconds 200
            $c = try { Invoke-RestMethod "$base/conversation" -TimeoutSec 5 } catch { $null }
            if ($c -and $c.active -and -not [string]::IsNullOrWhiteSpace($c.text)) { break }
        }
        if (-not $c -or -not $c.active) { $issues += "[$sc] $npc : dialogue did not open"; Post "end_dialogue"; Start-Sleep -Milliseconds 300; continue }
        $talked++

        # Greeting text check
        if ([string]::IsNullOrWhiteSpace($c.text)) { $issues += "[$sc] $npc : empty greeting text" }
        elseif ($c.text -match '[{]') { $issues += "[$sc] $npc : unresolved token in greeting: $($c.text)" }

        # Advance to options
        Post "dismiss_message"; Start-Sleep -Milliseconds 800
        $c = try { Invoke-RestMethod "$base/conversation" -TimeoutSec 5 } catch { $null }
        if ($c -and $c.active) {
            if (@($c.options).Count -eq 0) { $issues += "[$sc] $npc : no options after greeting" }
            foreach ($o in $c.options) {
                if ($o.text -match '[{]') { $issues += "[$sc] $npc : unresolved token in option: $($o.text)" }
            }
            # Exercise the profession option (block 0 / option 1) - just shows text, safe.
            Post "respond 1"; Start-Sleep -Milliseconds 700
            $c2 = try { Invoke-RestMethod "$base/conversation" -TimeoutSec 5 } catch { $null }
            if ($c2 -and $c2.active -and -not [string]::IsNullOrWhiteSpace($c2.text) -and $c2.text -match '[{]') {
                $issues += "[$sc] $npc : unresolved token in profession reply: $($c2.text)"
            }
            Post "dismiss_message"; Start-Sleep -Milliseconds 400
        }

        Post "end_dialogue"; Start-Sleep -Milliseconds 400
        $errs = try { (Invoke-RestMethod "$base/log?level=error&n=5" -TimeoutSec 5).messages } catch { @() }
        foreach ($e in $errs) { $issues += "[$sc] $npc : ERROR $($e.text)" }
        if (-not (Alive)) { $issues += "[$sc] $npc : CRASH during dialogue"; if (-not (Start-Game)) { break }; break }
    }
}

"`n========== CONVERSATION SHAKEOUT RESULT =========="
"Talked to $talked NPCs across $($scenarios.Count) chapters."
if ($issues.Count -eq 0) { "NO ISSUES." } else { "ISSUES ($($issues.Count)):"; $issues | ForEach-Object { "  $_" } }
Get-Process UAlbion* -ErrorAction SilentlyContinue | Stop-Process -Force
