# _regression_e2e.ps1 — automated e2e regression gate for every empirically-found-and-fixed
# defect from the 2026-07-10 coverage campaign (see _COVERAGE_MATRIX.md, passes 1-6).
# Each check re-verifies a specific fix through the HTTP harness so a regression is caught
# by CI-style tooling instead of waiting to be re-discovered by hand.
# Usage: .\_regression_e2e.ps1 [-Port 7878] [-NoLaunch]
param([int]$Port = 7878, [switch]$NoLaunch)

$ErrorActionPreference = 'Continue'
$Base = "http://localhost:$Port"
$repo = $PSScriptRoot
$exe  = Join-Path $repo 'build\UAlbion\bin\Release\net9.0\UAlbion.exe'
$script:results = @()
$script:n = 0

function E($body) {
    try { Invoke-RestMethod -Method POST "$Base/event/raw" -Body $body -ContentType text/plain } catch { $null }
}
function G($path) { try { Invoke-RestMethod "$Base$path" } catch { $null } }
function Check($name, $cond, $detail = '') {
    $script:n++
    $status = if ($cond) { 'PASS' } else { 'FAIL' }
    $script:results += [pscustomobject]@{ n=$script:n; status=$status; name=$name; detail=$detail }
    Write-Host ("{0,3} [{1}] {2}{3}" -f $script:n, $status, $name, ($(if ($detail -and -not $cond) { " — $detail" } else { '' })))
}

if (-not $NoLaunch) {
    Get-Process UAlbion -ErrorAction SilentlyContinue | Where-Object {
        (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine -match "--harness-http $Port"
    } | Stop-Process -Force -Confirm:$false
    Start-Sleep 1
    Start-Process -FilePath $exe -ArgumentList '-d3d','--mute','--harness-http',"$Port" -WorkingDirectory (Split-Path $exe) | Out-Null
    $deadline = (Get-Date).AddSeconds(60)
    do { Start-Sleep -Milliseconds 1500; $h = G '/healthz' } while (-not $h -and (Get-Date) -lt $deadline)
    if (-not $h) { Write-Host 'FATAL: game did not come up'; exit 1 }
}

# ---- R1: level-up gains + non-caster SP gate (SheetApplier.ApplyPerLevelGains) ----
E 'load_game 7' | Out-Null; Start-Sleep 3
$t0 = G '/sheet?id=PartySheet.Tom'
E 'change PartyMember.Tom Experience AddAmount 200' | Out-Null; Start-Sleep 1  # 400->600, crosses 550 (L6)
$t1 = G '/sheet?id=PartySheet.Tom'
Check 'R1a level-up fires on XP change (Tom L5->6)' ($t1.level -eq ($t0.level + 1)) "L$($t0.level)->L$($t1.level)"
Check 'R1b level-up grants LP + TP' ($t1.lpMax -gt $t0.lpMax -and $t1.tp -gt $t0.tp) "lpMax $($t0.lpMax)->$($t1.lpMax) tp $($t0.tp)->$($t1.tp)"
Check 'R1c NON-CASTER gains NO SP (IsSpellcaster gate)' ($t1.spMax -eq 0) "spMax=$($t1.spMax)"

# ---- R2: caster SP gain + level-50 cap ----
E 'load_game 3' | Out-Null; Start-Sleep 3
$s0 = G '/sheet?id=PartySheet.Sira'
E 'change PartyMember.Sira Experience AddAmount 60000' | Out-Null
E 'change PartyMember.Sira Experience AddAmount 60000' | Out-Null
E 'change PartyMember.Sira Experience AddAmount 60000' | Out-Null; Start-Sleep 1
$s1 = G '/sheet?id=PartySheet.Sira'
Check 'R2a caster gains SP on level-up' ($s1.spMax -gt $s0.spMax) "spMax $($s0.spMax)->$($s1.spMax)"
Check 'R2b level caps at exactly 50' ($s1.level -eq 50) "level=$($s1.level)"

# ---- R3: change_item seeds template charges (SheetApplier.ApplyItem) ----
E 'change_item PartyMember.Tom Item.FireRing AddAmount 1' | Out-Null; Start-Sleep 1
$inv = G '/inventory?id=PartyMember.Tom'
$fr = @($inv.items | Where-Object item -eq 'Item.FireRing')[0]
Check 'R3 event-granted FireRing arrives charged' ($fr -and [int]$fr.charges -gt 0) "charges=$($fr.charges)"

# ---- R4: mastery growth visible to later casts (InventoryChangedEvent after GrowMastery) ----
E 'svc_learn PartyMember.Sira DjiKas 12 5' | Out-Null; Start-Sleep 1
E 'change PartyMember.Sira Mana AddAmount 250' | Out-Null
E 'cast_spell PartyMember.Sira Spell.SleepSpores PartyMember.Rainer' | Out-Null; Start-Sleep 1
$m1 = @((G '/sheet?id=PartySheet.Sira').knownSpells | Where-Object spell -eq 'Spell.SleepSpores')[0].strength
E 'cast_spell PartyMember.Sira Spell.Regeneration PartyMember.Rainer' | Out-Null; Start-Sleep 1
E 'cast_spell PartyMember.Sira Spell.SleepSpores PartyMember.Rainer' | Out-Null; Start-Sleep 1
$m2 = @((G '/sheet?id=PartySheet.Sira').knownSpells | Where-Object spell -eq 'Spell.SleepSpores')[0].strength
Check 'R4 spell mastery grows across casts' ($m2 -gt $m1) "strength $m1->$m2"

# ---- R5: poison lifecycle (StatusConditionTicker + Cure service) ----
E 'load_game 7' | Out-Null; Start-Sleep 3
E 'change_status PartyMember.Tom Poisoned SetToMaximum' | Out-Null
$p0 = (G '/sheet?id=PartySheet.Tom').lp
E 'modify_hours AddAmount 3' | Out-Null; Start-Sleep 2
$p1 = (G '/sheet?id=PartySheet.Tom').lp
Check 'R5a poison drains LP per hour' ($p1 -lt $p0) "lp $p0->$p1"
E 'change PartyMember.Tom Gold AddAmount 100' | Out-Null
E 'place_action Cure 0 0 0 0 30 0' | Out-Null; Start-Sleep 1
$pc = (G '/sheet?id=PartySheet.Tom').conditions
Check 'R5b Cure service clears Poisoned' ($pc -notmatch 'Poisoned') "cond=$pc"

# ---- R6: leader-incapacity Tom revival (LeaderIncapacityWatcher, fcn.0003822d) ----
E 'change_status PartyMember.Tom Paralysed SetToMaximum' | Out-Null
E 'change_status PartyMember.Rainer Paralysed SetToMaximum' | Out-Null; Start-Sleep 1
$rv = G '/sheet?id=PartySheet.Tom'
Check 'R6a whole-party paralysis -> Tom self-rescues' ($rv.conditions -notmatch 'Paralysed') "cond=$($rv.conditions)"
E 'change PartyMember.Rainer Health SubtractAmount 99' | Out-Null
E 'change PartyMember.Tom Health SubtractAmount 99' | Out-Null; Start-Sleep 1
$rv2 = G '/sheet?id=PartySheet.Tom'
Check 'R6b out-of-combat wipe -> Tom revives at 1 LP' ($rv2.lp -ge 1 -and $rv2.conditions -notmatch 'Unconscious') "lp=$($rv2.lp) cond=$($rv2.conditions)"

# ---- R7: stale chains do not survive load (EventChainManager.AbortAllForLoad) ----
E 'synth_scenario maini' | Out-Null; Start-Sleep 4
E 'load_game 7' | Out-Null; Start-Sleep 3
$ch = G '/chains'
Check 'R7 no event chains survive load_game' (@($ch.chains).Count -eq 0) "chains=$(@($ch.chains).Count)"

# ---- R8: language barrier (ConversationManager gate, SYSTEXTS 540) ----
E 'new_game Map.TorontoBegin 31 76' | Out-Null; Start-Sleep 5
E 'teleport Map.HunterClan 20 20' | Out-Null; Start-Sleep 3
$npcs = (G '/npcs').npcs
$w = @($npcs | Where-Object { $_.id -eq 'NpcSheet.SebaiLiWrinn' })[0]
if ($w) { G "/npctalk?n=$($w.n)" | Out-Null; Start-Sleep 2 }
$cv = G '/conversation'
Check 'R8a Terran-only Tom refused by Iskai NPC' (-not $cv.active) "active=$($cv.active)"
E 'dismiss_message' | Out-Null
E 'load_game 3' | Out-Null; Start-Sleep 3
$npcs2 = (G '/npcs').npcs
$r0 = @($npcs2 | Where-Object { $_.id -match 'NpcSheet' })[0]
if ($r0) { G "/npctalk?n=$($r0.n)" | Out-Null; Start-Sleep 2 }
$cv2 = G '/conversation'
Check 'R8b Iskai-speaking Tom converses' ($cv2.active -eq $true) "active=$($cv2.active)"
E 'end_dialogue' | Out-Null; E 'dismiss_message' | Out-Null

# ---- R10: contact-combat flee re-arm (Npc2D/Npc3D _contactTriggered latch) ----
# MUST run before R9, which disables every Maini monster for the rest check.
E 'synth_scenario maini' | Out-Null; Start-Sleep 4
E 'teleport Map.Maini 52 37' | Out-Null
$deadline = (Get-Date).AddSeconds(20); $c = $null
do { Start-Sleep 2; $c = G '/combat' } while (-not $c.active -and (Get-Date) -lt $deadline)
Check 'R10a contact combat triggers' ($c.active -eq $true)
E 'end_combat Retreat' | Out-Null; Start-Sleep 2
$reenc = $false
for ($i = 0; $i -lt 5; $i++) { Start-Sleep 2; if ((G '/combat').active) { $reenc = $true; break } }
Check 'R10b no instant re-encounter after fleeing' (-not $reenc)

# ---- R9: rest-till-dawn lands exactly on 07:00 (GameState.OnRest snap) ----
$npcsM = (G '/npcs').npcs
foreach ($m in @($npcsM | Where-Object { $_.id -match 'MonsterGroup' })) { E "npc_off $($m.n)" | Out-Null }
E 'modify_hours AddAmount 12' | Out-Null; Start-Sleep 1   # push into night
$t = [datetime]::ParseExact(((G '/clock').time), 'MM/dd/yyyy HH:mm:ss', $null)
if ($t.Hour -ge 4 -and $t.Hour -lt 19) { E 'modify_hours AddAmount 8' | Out-Null; Start-Sleep 1 }
E 'rest 0' | Out-Null; Start-Sleep 4
$t2 = [datetime]::ParseExact(((G '/clock').time), 'MM/dd/yyyy HH:mm:ss', $null)
Check 'R9 till-dawn rest lands exactly 07:00:00' ($t2.Hour -eq 7 -and $t2.Minute -eq 0 -and $t2.Second -eq 0) "$($t2.ToString('HH:mm:ss'))"

# ---- R11: cursed item lifecycle (equip-trap + RemoveCurse) ----
E 'load_game 3' | Out-Null; Start-Sleep 3
E 'change_attribute PartyMember.Tom Strength SetToMaximum 0' | Out-Null
E 'change_item PartyMember.Tom Item.RedSword AddAmount 1' | Out-Null; Start-Sleep 1
$inv2 = G '/inventory?id=PartyMember.Tom'
$rs = @($inv2.items | Where-Object item -eq 'Item.RedSword')[0]
E "inv:pickup 1 PartyMember.Tom $($rs.slot)" | Out-Null
E 'inv:swap PartyMember.Tom RightHand' | Out-Null; Start-Sleep 1
$rh = @((G '/inventory?id=PartyMember.Tom').items | Where-Object slot -eq 'RightHand')[0]
Check 'R11a cursed equip traps the slot' ($rh.item -eq 'Item.RedSword' -and $rh.flags -match 'Cursed') "rh=$($rh.item) flags=$($rh.flags)"
E 'place_action RemoveCurse 0 0 0 0 100 0' | Out-Null; Start-Sleep 1
$rh2 = @((G '/inventory?id=PartyMember.Tom').items | Where-Object slot -eq 'RightHand')[0]
Check 'R11b RemoveCurse destroys cursed equipment' (-not $rh2 -or $rh2.item -ne 'Item.RedSword') "rh=$($rh2.item)"

# ---- R12: corrupt save fails gracefully (GameState.LoadGame guard) ----
$saveDir = Join-Path $repo 'ALBION\SAVES'
$src = Get-ChildItem $saveDir -Filter 'save.003' | Select-Object -First 1
$bytes = [IO.File]::ReadAllBytes($src.FullName)
[IO.File]::WriteAllBytes("$saveDir\SAVE.089", $bytes[0..1023])
$mapBefore = (G '/state').map
E 'load_game 89' | Out-Null; Start-Sleep 3
$h2 = G '/healthz'
Check 'R12 corrupt save: game survives, state kept' ($h2.ok -and (G '/state').map -eq $mapBefore) "map=$((G '/state').map)"
Remove-Item "$saveDir\SAVE.089" -Force -ErrorAction SilentlyContinue

# ---- R13: Drinno pit both arms (chain text-wait + teleport) ----
E 'synth_scenario drinno' | Out-Null; Start-Sleep 4
E 'teleport Map.Drinno2 12 9' | Out-Null; Start-Sleep 3
E 'trigger_tile Normal 5 27' | Out-Null; Start-Sleep 2
E 'dismiss_message' | Out-Null; Start-Sleep 3
Check 'R13 pit step-on falls to Drinno3' ((G '/state').map -eq 'Map.Drinno3') "map=$((G '/state').map)"

# ---- R14: conversation flow + 0xFF service-text sentinel (PlaceActionManager) ----
E 'load_game 92' | Out-Null; Start-Sleep 3
E 'change_language PartyMember.Tom Iskai SetToMaximum' | Out-Null
$npcsZ = (G '/npcs').npcs
$zirr = @($npcsZ | Where-Object { $_.id -eq 'NpcSheet.Zirr' })[0]
if ($zirr) {
    G "/npctalk?n=$($zirr.n)" | Out-Null; Start-Sleep 2
    E 'dismiss_message' | Out-Null; Start-Sleep 2
    $uiC = (G '/ui').elements
    $hasOptions = @($uiC | Where-Object { $_.kind -eq 'ConversationOptionsWindow' }).Count -gt 0
    $hasAnswer = @($uiC | Where-Object { $_.label -match 'place to stay the night' }).Count -gt 0
    Check 'R14a conversation presents numbered answers + menu' ($hasOptions -and $hasAnswer)
    E 'respond 1' | Out-Null; Start-Sleep 2
    $uiC2 = (G '/ui').elements
    $missing = @($uiC2 | Where-Object { $_.label -match 'MISSING' }).Count
    $really = @($uiC2 | Where-Object { $_.label -match 'Really rest' }).Count -gt 0
    Check 'R14b 0xFF confirm shows default text, no MISSING STRING' ($missing -eq 0 -and $really)
    E 'respond 2' | Out-Null; Start-Sleep 1  # No — don't actually sleep
    E 'end_dialogue' | Out-Null; E 'dismiss_message' | Out-Null
} else {
    Check 'R14a conversation presents numbered answers + menu' $false 'Zirr not found'
}

# ---- R15: portrait condition overlay (StatusBarPortrait) ----
E 'load_game 4' | Out-Null; Start-Sleep 3   # Tom+Rainer Intoxicated (conscious)
$uiP = (G '/ui').elements
$fx4 = @($uiP | Where-Object { $_.label -match 'CharEffect' }).Count
Check 'R15a conscious afflictions show NO portrait effect' ($fx4 -eq 0) "CharEffect elements=$fx4"
E 'load_game 5' | Out-Null; Start-Sleep 3   # Tom+Rainer Unconscious
$uiP2 = (G '/ui').elements
$fxDead = @($uiP2 | Where-Object { $_.label -match 'CharEffect3' }).Count
$fxOther = @($uiP2 | Where-Object { $_.label -match 'CharEffect1|CharEffect2' }).Count
Check 'R15b KO members show static dead face only' ($fxDead -ge 2 -and $fxOther -eq 0) "dead=$fxDead other=$fxOther"

# ---- summary ----
$fails = @($script:results | Where-Object status -eq 'FAIL')
Write-Host ''
Write-Host ("Regression: {0}/{1} pass" -f (@($script:results).Count - $fails.Count), @($script:results).Count) -ForegroundColor $(if ($fails.Count -eq 0) { 'Green' } else { 'Red' })
if ($fails.Count -gt 0) { $fails | ForEach-Object { Write-Host ("  FAIL {0} — {1}" -f $_.name, $_.detail) -ForegroundColor Red }; exit 1 }
exit 0
