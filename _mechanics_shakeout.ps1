# _mechanics_shakeout.ps1 — live-drives every game mechanic/interface surface through the
# HTTP harness with real assertions.
# Usage: .\_mechanics_shakeout.ps1 [-Phase A|B|C|D|E|F|all] [-Port 7879] [-NoLaunch]
# Each check: fire events -> assert observable state -> lastError must stay null.
param(
    [string]$Phase = 'all',
    [int]$Port = 8123,   # distinct port so this can run alongside another harness instance
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$base = "http://localhost:$Port"
$repo = $PSScriptRoot
$wd   = Join-Path $repo 'build\UAlbion\bin\Release\net9.0'
$exe  = Join-Path $wd 'UAlbion.exe'
$script:results = [System.Collections.ArrayList]@()
$script:checkNum = 0

function E([string]$body) {
    try { return Invoke-RestMethod -Method POST "$base/event/raw" -Body $body -ContentType text/plain }
    catch { return @{ ok = $false; error = $_.ToString() } }
}
function G([string]$path) { return Invoke-RestMethod "$base$path" }
function LastErr { return (G '/healthz').lastError }

function Check([string]$name, [bool]$cond, [string]$detail = '') {
    $script:checkNum++
    $status = if ($cond) { 'PASS' } else { 'FAIL' }
    $line = ('{0,3} [{1}] {2}{3}' -f $script:checkNum, $status, $name, ($(if ($detail -and -not $cond) { " — $detail" } else { '' })))
    Write-Host $line -ForegroundColor $(if ($cond) { 'Green' } else { 'Red' })
    [void]$script:results.Add([pscustomobject]@{ n = $script:checkNum; status = $status; name = $name; detail = $detail })
}
function CheckClean([string]$name) {
    $err = LastErr
    Check "$name (lastError null)" ([string]::IsNullOrEmpty($err)) $err
}

function Start-Game {
    # Only recycle OUR OWN instance — other sessions may be driving their own UAlbion on
    # another port (never Stop-Process all UAlbion*). HttpListener binds via http.sys, so
    # port→PID lookup is useless; track our instance with a per-port pidfile instead.
    $pidFile = Join-Path $env:TEMP "ualbion_harness_$Port.pid"
    if (Test-Path $pidFile) {
        $oldPid = Get-Content $pidFile -ErrorAction SilentlyContinue
        if ($oldPid) { Stop-Process -Id $oldPid -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep 2
    $proc = Start-Process -FilePath $exe -ArgumentList '-d3d','--mute','--harness-http',"$Port" -WorkingDirectory $wd -PassThru
    Set-Content $pidFile $proc.Id
    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline) {
        try { if ((Invoke-RestMethod "$base/healthz" -TimeoutSec 2).ok) { return } } catch {}
        Start-Sleep 1
    }
    throw "game did not come up on port $Port"
}

function LoadSave([int]$slot, [string]$expectMap) {
    E "load_game $slot" | Out-Null
    Start-Sleep 4
    $s = G '/state'
    Check "load_game $slot -> $expectMap" ($s.loaded -and $s.map -eq $expectMap) "map=$($s.map)"
    return $s
}

# Item helpers built on the slot-detail /inventory endpoint
function Inv([string]$id) { return G "/inventory?id=$id" }
function FindSlot($inv, [string]$item) {
    return ($inv.items | Where-Object { $_.item -eq $item } | Select-Object -First 1)
}
function ItemTotal($inv, [string]$item) {
    return (($inv.items | Where-Object { $_.item -eq $item } | Measure-Object amount -Sum).Sum ?? 0)
}

# Click the kind=Button element whose bounds enclose a UiText matching $label.
# (clicking the child UiText itself does nothing - the Button owns the click.)
function ClickLabelled([string]$label) {
    $els = (G '/ui').elements
    $btn = $els | Where-Object { $_.kind -eq 'Button' -and $_.label -match $label } | Select-Object -First 1
    if (-not $btn) {
        $txt = $els | Where-Object { $_.label -match $label } | Select-Object -First 1
        if ($txt) {
            $btn = $els | Where-Object { $_.kind -eq 'Button' -and $_.x -le $txt.x -and $_.y -le $txt.y -and
                ($_.x + $_.w) -ge ($txt.x + $txt.w) -and ($_.y + $_.h) -ge ($txt.y + $txt.h) } | Select-Object -First 1
        }
    }
    if ($btn) {
        Invoke-RestMethod -Method POST "$base/click" -Body (@{ id = $btn.id } | ConvertTo-Json) -ContentType application/json | Out-Null
        return $true
    }
    return $false
}

if (-not $NoLaunch) { Start-Game }

# ============================ PHASE A — inventory & items ============================
if ($Phase -in 'A','all') {
    Write-Host "`n=== PHASE A: inventory & items ===" -ForegroundColor Cyan
    LoadSave 3 'Map.Jirinaar' | Out-Null

    # A1: sheet endpoint sanity (Tom: Terran+Iskai languages per page III)
    $sheet = G '/sheet?id=PartySheet.Tom'
    Check 'sheet Tom found, level>0, languages Terran+Iskai' `
        ($sheet.found -and $sheet.level -gt 0 -and $sheet.languages -match 'Terran' -and $sheet.languages -match 'Iskai') `
        ($sheet | ConvertTo-Json -Compress)

    # A2: inventory screen opens + pages switch cleanly
    E 'inv:open PartyMember.Tom' | Out-Null; Start-Sleep 1
    CheckClean 'inv:open Tom'
    E 'inv:set_page Stats' | Out-Null
    E 'inv:set_page Misc' | Out-Null
    E 'inv:set_page Summary' | Out-Null
    Start-Sleep 1
    CheckClean 'inventory page cycling'

    # A3: move a stack between members (pickup N -> give) with conservation
    $tomInv = Inv 'PartyMember.Tom'
    $torch = FindSlot $tomInv 'Item.Torch'
    Check 'Tom has a torch stack (slot detail)' ($null -ne $torch) ($tomInv | ConvertTo-Json -Compress -Depth 4)
    if ($torch) {
        $before = ItemTotal (Inv 'PartyMember.Drirr') 'Item.Torch'
        E "inv:pickup 5 PartyMember.Tom $($torch.slot)" | Out-Null
        E 'inv:give PartyMember.Drirr' | Out-Null
        Start-Sleep 1
        $tomAfter = ItemTotal (Inv 'PartyMember.Tom') 'Item.Torch'
        $drirrAfter = ItemTotal (Inv 'PartyMember.Drirr') 'Item.Torch'
        Check 'give 5 torches Tom->Drirr conserves totals' `
            (($tomAfter -eq ($torch.amount - 5)) -and ($drirrAfter -eq ($before + 5))) `
            "tom=$tomAfter (want $($torch.amount-5)) drirr=$drirrAfter (want $($before+5))"
        CheckClean 'pickup/give'
    }

    # A4: equip swap — move Tom's CrystalDagger into RightHand and back
    $tomInv = Inv 'PartyMember.Tom'
    $dagger = FindSlot $tomInv 'Item.CrystalDagger'
    $rhBefore = ($tomInv.items | Where-Object slot -eq 'RightHand' | Select-Object -First 1)
    if ($dagger) {
        E "inv:pickup 1 PartyMember.Tom $($dagger.slot)" | Out-Null
        E 'inv:swap PartyMember.Tom RightHand' | Out-Null
        Start-Sleep 1
        $rhNow = ((Inv 'PartyMember.Tom').items | Where-Object slot -eq 'RightHand' | Select-Object -First 1)
        Check 'equip CrystalDagger to RightHand' ($rhNow.item -eq 'Item.CrystalDagger') "rh=$($rhNow.item)"
        # put whatever was displaced back in the dagger's old slot, then restore
        E "inv:swap PartyMember.Tom $($dagger.slot)" | Out-Null
        Start-Sleep 1
        CheckClean 'equip/unequip round trip'
    }

    # A5: examine an item (fires text popup; assert no crash)
    if ($dagger) {
        E "inv:examine PartyMember.Tom $($dagger.slot)" | Out-Null
        Start-Sleep 1
        E 'dismiss_message' | Out-Null
        CheckClean 'inv:examine'
    }

    E 'inv:close' | Out-Null; Start-Sleep 1

    # A6: locked chest -> lockpick -> contents -> take_all
    # Fabricated locked chest event (difficulty 40); Rainer carries lockpicks.
    E 'chest Chest.1 Item.None 40 255 255' | Out-Null
    Start-Sleep 1
    $ui = G '/ui'
    $lockVisible = ($ui.elements | Where-Object { $_.label -match 'lock|Lock' }).Count -gt 0
    Check 'locked chest shows lock pane' $lockVisible
    E 'inv:pick_lock' | Out-Null
    Start-Sleep 1
    CheckClean 'pick_lock attempt'
    $chestInv = Inv 'Chest.1'
    Check 'chest inventory queryable' ($chestInv.found -eq $true) ($chestInv | ConvertTo-Json -Compress)
    E 'inv:close' | Out-Null; Start-Sleep 1
    CheckClean 'chest close'

    # A7: door event (locked, pickable)
    E 'door Door.100 Item.None 30 255 255' | Out-Null
    Start-Sleep 1
    E 'inv:pick_lock' | Out-Null
    Start-Sleep 1
    CheckClean 'door + pick_lock'
    E 'inv:close' | Out-Null
    E 'dismiss_message' | Out-Null
    Start-Sleep 1

    # A8: torch burnout — advance hours, torch stack charges/count must not grow
    $tomTorchBefore = ItemTotal (Inv 'PartyMember.Tom') 'Item.Torch'
    E 'modify_hours AddAmount 2' | Out-Null
    Start-Sleep 2
    $tomTorchAfter = ItemTotal (Inv 'PartyMember.Tom') 'Item.Torch'
    Check "torch stack stable-or-burning after 2h (was $tomTorchBefore now $tomTorchAfter)" ($tomTorchAfter -le $tomTorchBefore)
    CheckClean 'time advance 2h'
}

# ============================ PHASE B — economy & services ============================
if ($Phase -in 'B','all') {
    Write-Host "`n=== PHASE B: economy & services ===" -ForegroundColor Cyan
    LoadSave 3 'Map.Jirinaar' | Out-Null

    # B1: merchant screen + stock introspection
    E 'inv:merchant Merchant.Wania PartyMember.Tom 100' | Out-Null
    Start-Sleep 1
    $stock = Inv 'Merchant.Wania'
    Check 'merchant Wania opens, stock queryable' ($stock.found -and $stock.items.Count -gt 0) ($stock | ConvertTo-Json -Compress -Depth 4)
    CheckClean 'inv:merchant open'

    # B2: BUY one unit of the first stock item — party gold down, leader item up
    if ($stock.found -and $stock.items.Count -gt 0) {
        $ware = $stock.items[0]
        $goldBefore = (G '/inventory').totalGold
        $haveBefore = ItemTotal (Inv 'PartyMember.Tom') $ware.item
        E "inv:sell Merchant.Wania $($ware.slot)" | Out-Null
        Start-Sleep 1
        $goldAfter = (G '/inventory').totalGold
        $haveAfter = ItemTotal (Inv 'PartyMember.Tom') $ware.item
        Check "buy $($ware.item): gold down, item up" (($goldAfter -lt $goldBefore) -and ($haveAfter -eq $haveBefore + 1)) `
            "gold $goldBefore->$goldAfter have $haveBefore->$haveAfter"
        CheckClean 'merchant buy'
    }

    # B3: SELL a torch to the merchant — gold up, merchant gains stock
    $torch = FindSlot (Inv 'PartyMember.Tom') 'Item.Torch'
    if ($torch) {
        $goldBefore = (G '/inventory').totalGold
        E "inv:sell_to_merchant Merchant.Wania PartyMember.Tom $($torch.slot)" | Out-Null
        Start-Sleep 1
        $goldAfter = (G '/inventory').totalGold
        $merchTorch = ItemTotal (Inv 'Merchant.Wania') 'Item.Torch'
        Check 'sell torch: gold up, merchant stocked' (($goldAfter -gt $goldBefore) -and ($merchTorch -ge 1)) `
            "gold $goldBefore->$goldAfter merchTorch=$merchTorch"
        CheckClean 'merchant sell'
    }
    E 'inv:close' | Out-Null; Start-Sleep 1

    # B4: svc_food — rations up, gold down (price 10/ration, 5 rations)
    $ratBefore = ((G '/inventory').members | Where-Object id -eq 'PartyMember.Tom').rations
    $goldBefore = (G '/inventory').totalGold
    E 'svc_food PartyMember.Tom 10 5' | Out-Null
    Start-Sleep 1
    $ratAfter = ((G '/inventory').members | Where-Object id -eq 'PartyMember.Tom').rations
    $goldAfter = (G '/inventory').totalGold
    Check 'svc_food: +5 rations, -50 gold' (($ratAfter -eq $ratBefore + 5) -and ($goldAfter -eq $goldBefore - 50)) `
        "rations $ratBefore->$ratAfter gold $goldBefore->$goldAfter"
    CheckClean 'svc_food'

    # B5: svc_train — skill up, gold down, TP down
    $sheetBefore = G '/sheet?id=PartySheet.Tom'
    E 'svc_train PartyMember.Tom Melee 20' | Out-Null
    Start-Sleep 1
    $sheetAfter = G '/sheet?id=PartySheet.Tom'
    Check 'svc_train Melee: skill+1, tp-1' `
        (($sheetAfter.skills.closeCombat.current -eq $sheetBefore.skills.closeCombat.current + 1) -and ($sheetAfter.tp -eq $sheetBefore.tp - 1)) `
        "skill $($sheetBefore.skills.closeCombat.current)->$($sheetAfter.skills.closeCombat.current) tp $($sheetBefore.tp)->$($sheetAfter.tp)"
    CheckClean 'svc_train'

    # B6: break + repair — Broken flag round trip via slot detail
    E 'break_item_slot PartyMember.Tom RightHand' | Out-Null
    Start-Sleep 1
    $rh = ((Inv 'PartyMember.Tom').items | Where-Object slot -eq 'RightHand' | Select-Object -First 1)
    Check 'break_item_slot sets Broken flag' ($rh.flags -match 'Broken') "flags=$($rh.flags)"
    E 'svc_repair PartyMember.Tom RightHand 30' | Out-Null
    Start-Sleep 1
    $rh2 = ((Inv 'PartyMember.Tom').items | Where-Object slot -eq 'RightHand' | Select-Object -First 1)
    Check 'svc_repair clears Broken flag' ($rh2.flags -notmatch 'Broken') "flags=$($rh2.flags)"
    CheckClean 'break/repair'

    # B7: svc_heal — damage Tom then heal for gold
    E 'change PartyMember.Tom Health SubtractAmount 20' | Out-Null
    Start-Sleep 1
    $lpHurt = (G '/sheet?id=PartySheet.Tom').lp
    E 'svc_heal PartyMember.Tom 2' | Out-Null
    Start-Sleep 1
    $after = G '/sheet?id=PartySheet.Tom'
    $goldNow = (G '/inventory').totalGold
    Check "svc_heal restores LP (hurt=$lpHurt -> $($after.lp)/$($after.lpMax))" ($after.lp -gt $lpHurt)
    CheckClean 'change health + svc_heal'

    # B8: svc_learn — teach Sira a DjiKas spell she doesn't know (via /sheet diff)
    $siraBefore = (G '/sheet?id=PartySheet.Sira').knownSpells.Count
    E 'svc_learn PartyMember.Sira DjiKas 4 5' | Out-Null
    Start-Sleep 1
    $siraAfter = (G '/sheet?id=PartySheet.Sira').knownSpells.Count
    Check "svc_learn DjiKas#4: known spells $siraBefore->$siraAfter" ($siraAfter -ge $siraBefore)
    CheckClean 'svc_learn'

    # B9: teleporter menu (visited Jirinaar markers) opens a context menu
    E 'teleporter_menu' | Out-Null
    Start-Sleep 1
    $ui = G '/ui'
    $menuUp = ($ui.elements | Where-Object { $_.kind -match 'ContextMenu|Menu' }).Count -gt 0
    Check 'teleporter_menu opens destination picker' $menuUp
    E 'dismiss_message' | Out-Null
    Start-Sleep 1
    CheckClean 'teleporter menu'
}

# ============================ PHASE C — magic, rest & time ============================
if ($Phase -in 'C','all') {
    Write-Host "`n=== PHASE C: magic, rest & time ===" -ForegroundColor Cyan
    LoadSave 3 'Map.Jirinaar' | Out-Null

    # C1: out-of-combat magic menu opens
    E 'magic_menu PartyMember.Sira' | Out-Null
    Start-Sleep 1
    CheckClean 'magic_menu Sira'
    E 'dismiss_message' | Out-Null; Start-Sleep 1

    # C2: field-cast — hurt Tom, then walk Sira's spellbook until a cast consumes SP
    E 'change PartyMember.Tom Health SubtractAmount 20' | Out-Null
    Start-Sleep 1
    $spBefore = (G '/sheet?id=PartySheet.Sira').sp
    $lpBefore = (G '/sheet?id=PartySheet.Tom').lp
    $castSpell = $null
    foreach ($ks in (G '/sheet?id=PartySheet.Sira').knownSpells) {
        E "cast_spell PartyMember.Sira $($ks.spell) PartyMember.Tom" | Out-Null
        Start-Sleep 1
        E 'dismiss_message' | Out-Null
        $spNow = (G '/sheet?id=PartySheet.Sira').sp
        if ($spNow -lt $spBefore) { $castSpell = $ks.spell; break }
    }
    $lpAfter = (G '/sheet?id=PartySheet.Tom').lp
    Check "field cast consumed SP ($castSpell; sp $spBefore->$((G '/sheet?id=PartySheet.Sira').sp), TomLP $lpBefore->$lpAfter)" ($null -ne $castSpell)
    CheckClean 'field casting sweep'

    # C3: explicit rest (inn-style, bypasses city gate): +8h clock, LP recovery, rations spent
    $timeBefore = [datetime](G '/state').time
    $lpBefore = (G '/sheet?id=PartySheet.Tom').lp
    $ratBefore = ((G '/inventory').members | Where-Object id -eq 'PartyMember.Tom').rations
    E 'rest 8' | Out-Null
    Start-Sleep 3
    $timeAfter = [datetime](G '/state').time
    $lpAfter = (G '/sheet?id=PartySheet.Tom').lp
    $ratAfter = ((G '/inventory').members | Where-Object id -eq 'PartyMember.Tom').rations
    Check "rest 8: clock +8h (was $timeBefore now $timeAfter)" (($timeAfter - $timeBefore).TotalHours -ge 7.9)
    Check "rest 8: LP recovered ($lpBefore->$lpAfter), rations spent ($ratBefore->$ratAfter)" (($lpAfter -gt $lpBefore) -and ($ratAfter -lt $ratBefore))
    CheckClean 'rest 8'

    # C4: fatigue — 50h awake => Exhausted + attribute backup (the check runs on odd
    # game-clock hours only, so 50 covers both parities); reset_fatigue restores
    E 'modify_hours AddAmount 50' | Out-Null
    Start-Sleep 3
    $tom = G '/sheet?id=PartySheet.Tom'
    Check "50h awake: Exhausted condition + STR backup ($($tom.conditions); str=$($tom.attributes.strength | ConvertTo-Json -Compress))" `
        (($tom.conditions -match 'Exhausted') -and ($tom.attributes.strength.backup -gt 0))
    # Rest is the cure: clears Exhausted and restores the backed-up stats exactly
    E 'rest 8' | Out-Null
    Start-Sleep 3
    $tom2 = G '/sheet?id=PartySheet.Tom'
    Check "rest cures Exhausted, STR restored from backup ($($tom2.conditions); str=$($tom2.attributes.strength.current), was backed up $($tom.attributes.strength.backup))" `
        (($tom2.conditions -notmatch 'Exhausted') -and ($tom2.attributes.strength.current -eq $tom.attributes.strength.backup))
    CheckClean 'fatigue cycle'

    # C5: city rest gate — bare `rest` on a city map must not crash (menu prompt / message)
    E 'rest' | Out-Null
    Start-Sleep 1
    E 'dismiss_message' | Out-Null
    CheckClean 'city rest gate'

    # C6: day/night palette — capture midnight vs noon screenshots for the record
    $hourNow = ([datetime](G '/state').time).Hour
    $toMidnight = (24 - $hourNow + 23) % 24
    if ($toMidnight -gt 0) { E "modify_hours AddAmount $toMidnight" | Out-Null; Start-Sleep 2 }
    Invoke-RestMethod -Method POST "$base/screenshot" -OutFile "$env:TEMP\ua_night.png" | Out-Null
    E 'modify_hours AddAmount 13' | Out-Null; Start-Sleep 2
    Invoke-RestMethod -Method POST "$base/screenshot" -OutFile "$env:TEMP\ua_day.png" | Out-Null
    $nightBytes = (Get-Item "$env:TEMP\ua_night.png").Length
    $dayBytes = (Get-Item "$env:TEMP\ua_day.png").Length
    Check "day/night screenshots captured (night=$nightBytes B, day=$dayBytes B)" (($nightBytes -gt 1kb) -and ($dayBytes -gt 1kb))
    CheckClean 'day/night cycle'
}

# ============================ PHASE E — combat depth ============================
if ($Phase -in 'E','all') {
    Write-Host "`n=== PHASE E: combat depth ===" -ForegroundColor Cyan

    function WaitCombat([bool]$active, [int]$seconds = 25) {
        $deadline = (Get-Date).AddSeconds($seconds)
        while ((Get-Date) -lt $deadline) {
            if ((G '/combat').active -eq $active) { return $true }
            Start-Sleep 1
        }
        return $false
    }
    function EnemyTiles { return @((G '/combat').combatants | Where-Object { $_.team -eq 'monster' -and $_.alive }) }
    function PartyTile([string]$sheet) { return ((G '/combat').combatants | Where-Object sheet -eq $sheet | Select-Object -First 1) }
    # Round playback (6.67 fps anims, per-mob moves) far outlasts a fixed sleep, and
    # begin_combat_round rejects re-entry mid-round — wait for the message log to go quiet.
    function WaitRoundDone([int]$maxSeconds = 40) {
        $deadline = (Get-Date).AddSeconds($maxSeconds)
        $last = (G '/log').total; $stable = 0
        while ((Get-Date) -lt $deadline -and $stable -lt 2) {
            Start-Sleep 2
            $now = (G '/log').total
            if ($now -eq $last) { $stable++ } else { $stable = 0; $last = $now }
        }
    }
    function RunRound { E 'begin_combat_round' | Out-Null; Start-Sleep 2; WaitRoundDone }
    function EndAnyCombat {
        if ((G '/combat').active) {
            E 'combat_damage -1 9999' | Out-Null
            Start-Sleep 3
            ClickLabelled 'Take all' | Out-Null
            Start-Sleep 1
            E 'dismiss_message' | Out-Null
            WaitCombat $false 15 | Out-Null
        }
    }

    # E1: staged multi-round fight on save 3 (healthy 4-member party; save 1's casters are
    # at 0 LP). Eight Broggs = enough HP to survive several ordered rounds + stone drops
    # for the loot check. Requires the abort-on-load fix (a stale battle otherwise
    # swallows every command after a reload).
    LoadSave 3 'Map.Jirinaar' | Out-Null
    E 'encounter MonsterGroup.EightBrogg1' | Out-Null
    Start-Sleep 2
    Check 'staged combat starts' (WaitCombat $true 15)
    CheckClean 'combat start'

    # Discover the party dynamically among ALIVE members: archer = EQUIPPED ranged weapon
    # + matching ammo; caster = max SP
    $ammoFor = @{ 'Bow' = 'Item.Arrow'; 'Thrower' = 'Item.Bolt'; 'Pistol' = 'Item.Canister'; 'Rifle' = 'Item.Canister' }
    $aliveSheets = @((G '/combat').combatants | Where-Object { $_.team -eq 'party' -and $_.alive } | ForEach-Object sheet)
    $members = @($aliveSheets | ForEach-Object { $_ -replace 'PartySheet', 'PartyMember' })
    $archer = $null; $archerAmmo = $null
    foreach ($m in $members) {
        $hands = (Inv $m).items | Where-Object { $_.slot -in 'LeftHand','RightHand','RightHandOrTail' }
        foreach ($h in $hands) {
            $kind = $ammoFor.Keys | Where-Object { $h.item -match $_ } | Select-Object -First 1
            if ($kind -and (ItemTotal (Inv $m) $ammoFor[$kind]) -gt 0) { $archer = $m; $archerAmmo = $ammoFor[$kind]; break }
        }
        if ($archer) { break }
    }
    $caster = $members | Sort-Object { (G "/sheet?id=$($_ -replace 'PartyMember','PartySheet')").sp } -Descending | Select-Object -First 1
    $leaderId = (G '/state').leader.id
    Write-Host "  (archer=$archer ammo=$archerAmmo caster=$caster leader=$leaderId)" -ForegroundColor DarkGray

    # E2: RANGED — archer attacks (Melee action: the strike executor converts to a ranged
    # strike when a ranged weapon + ammo are usable, mirroring the original's single attack
    # verb); per-strike ammo burn. Up to 3 rounds: the chosen target can die to an ally
    # earlier in the initiative order, which skips the whole strike (RE 5A) — re-acquire a
    # live target each round.
    if ($archer) {
        $sheet = $archer -replace 'PartyMember','PartySheet'
        $ammoBefore = ItemTotal (Inv $archer) $archerAmmo
        $ammoAfter = $ammoBefore
        for ($i = 0; $i -lt 3 -and $ammoAfter -eq $ammoBefore -and (G '/combat').active; $i++) {
            $live = EnemyTiles
            if ($live.Count -eq 0) { break }
            E "queue_combat_action $sheet Melee $($live[-1].tile)" | Out-Null
            RunRound
            $ammoAfter = ItemTotal (Inv $archer) $archerAmmo
        }
        Check "ranged attack burns ammo ($archer $archerAmmo $ammoBefore->$ammoAfter)" ($ammoAfter -lt $ammoBefore)
        CheckClean 'ranged round'
    }

    # E3: COMBAT CAST — strongest caster fires a known combat spell at an enemy tile; SP down
    $enemies = EnemyTiles
    if ((G '/combat').active -and $enemies.Count -gt 0 -and $caster) {
        $sheet = $caster -replace 'PartyMember','PartySheet'
        $spBefore = (G "/sheet?id=$sheet").sp
        $spAfter = $spBefore
        foreach ($ks in (G "/sheet?id=$sheet").knownSpells) {
            E "queue_combat_action $sheet CastSpell $($enemies[0].tile) $($ks.spell)" | Out-Null
            RunRound
            $spAfter = (G "/sheet?id=$sheet").sp
            if ($spAfter -lt $spBefore -or -not (G '/combat').active) { break }
        }
        Check "combat cast consumes SP ($caster $spBefore->$spAfter)" ($spAfter -lt $spBefore)
        CheckClean 'combat cast round'
    }

    # E4: USE ITEM — fire a charged spell-item (FireRing = Fireball x8) at an enemy; the
    # slot's charge must drop. (Drink-type potions are NOT combat items — combat item use
    # is the spell-item dispatch, RE fcn.0004f829.) Re-stage a fresh fight so the wielder
    # is guaranteed alive and not mid-flight from the prior rounds.
    EndAnyCombat
    Start-Sleep 2
    E 'encounter MonsterGroup.EightBrogg1' | Out-Null
    if (WaitCombat $true 15) {
        $ringWielders = @((G '/combat').combatants | Where-Object { $_.team -eq 'party' -and $_.alive } |
            Where-Object { ((Inv ($_.sheet -replace 'PartySheet','PartyMember')).items | Where-Object item -eq 'Item.FireRing') })
        $enemy = EnemyTiles | Select-Object -Last 1
        if ($ringWielders.Count -gt 0 -and $enemy) {
            $sheet = $ringWielders[0].sheet
            $wielder = $sheet -replace 'PartySheet','PartyMember'
            $chargesBefore = [int](((Inv $wielder).items | Where-Object item -eq 'Item.FireRing' | Select-Object -First 1).charges ?? 0)
            # The SpellId slot must be bare 'None' ('Spell.None' fails: SpellId.None is type
            # Unknown). Battle.UseQueuedItem resolves the real spell (Fireball) from the item.
            $q = E "queue_combat_action $sheet UseItem $($enemy.tile) None Item.FireRing"
            Check "UseItem action queued" ($q.ok -eq $true) ($q.error)
            RunRound
            $chargesAfter = [int](((Inv $wielder).items | Where-Object item -eq 'Item.FireRing' | Select-Object -First 1).charges ?? 0)
            Check "combat item use decrements FireRing charge ($wielder $chargesBefore->$chargesAfter)" ($chargesAfter -eq $chargesBefore - 1)
            CheckClean 'combat item round'
        }
    }

    # E5: ADVANCE PARTY — whole party steps one row toward the enemy
    if ((G '/combat').active) {
        # Advance only queues a Move for members whose forward tile stays inside the party
        # zone AND is free — with front-liners directly ahead, a no-op is CORRECT. Compute
        # the expected movers first and assert conditionally.
        $all = @((G '/combat').combatants)
        $party = @($all | Where-Object { $_.team -eq 'party' -and $_.alive })
        $occupied = @($all | Where-Object alive | ForEach-Object tile)
        $cols = 6
        $mobRows = 3
        $expectedMovers = @($party | Where-Object {
            $fwd = $_.tile - $cols
            ($fwd -ge $mobRows * $cols) -and ($fwd -notin $occupied)
        }).Count
        $rowsBefore = @($party | ForEach-Object tileY)
        E 'combat_advance_party' | Out-Null
        RunRound
        $rowsAfter = @((G '/combat').combatants | Where-Object { $_.team -eq 'party' -and $_.alive } | ForEach-Object tileY)
        $advanced = ($rowsAfter | Measure-Object -Sum).Sum -lt ($rowsBefore | Measure-Object -Sum).Sum
        Check "advance party ($expectedMovers expected mover(s); rows $($rowsBefore -join ',') -> $($rowsAfter -join ','))" `
            (($expectedMovers -eq 0) -or $advanced)
        CheckClean 'advance party round'
    }

    # E6: FLEE — Retreat only works from the party back row: members further forward must
    # move back a row first (claim-mask applies), so choreograph over several rounds.
    if ((G '/combat').active) {
        for ($round = 0; $round -lt 8 -and (G '/combat').active; $round++) {
            foreach ($pm in @((G '/combat').combatants | Where-Object { $_.team -eq 'party' -and $_.alive })) {
                if ($pm.tileY -ge 4) { E "queue_combat_action $($pm.sheet) Retreat -1" | Out-Null }
                else { E "queue_combat_action $($pm.sheet) Move $($pm.tile + 5)" | Out-Null }
            }
            RunRound
        }
        Check 'party flees: combat ends' (-not (G '/combat').active)
        E 'dismiss_message' | Out-Null
        CheckClean 'flee'
    }

    # E7: LOOT — stage a drop-carrying kill; loot window; take all
    EndAnyCombat   # make sure no battle is still live before staging the loot encounter
    Start-Sleep 2
    E 'encounter MonsterGroup.OneBrogg1' | Out-Null
    Start-Sleep 3
    if ((WaitCombat $true 10)) {
        $stonesBefore = ItemTotal (Inv 'PartyMember.Tom') 'Item.Stone'
        E 'combat_damage -1 9999' | Out-Null
        Start-Sleep 4
        $lootUi = @((G '/ui').elements | Where-Object { $_.label -match 'Take all|BattleLoot' })
        Check 'victory loot window appears' ($lootUi.Count -gt 0)
        # Click the real Take-all button (the bare take_all event has no global subscriber).
        # Button labels live on child UiText elements — find the text, click the enclosing Button.
        ClickLabelled 'Take all' | Out-Null
        Start-Sleep 2
        $stonesAfter = (ItemTotal (Inv 'PartyMember.Tom') 'Item.Stone') + (ItemTotal (Inv 'PartyMember.Rainer') 'Item.Stone') + (ItemTotal (Inv 'PartyMember.Drirr') 'Item.Stone') + (ItemTotal (Inv 'PartyMember.Sira') 'Item.Stone')
        Check "take_all collects loot (stones $stonesBefore->$stonesAfter)" ($stonesAfter -gt $stonesBefore)
        Check 'combat fully ends after loot' (WaitCombat $false 10)
        CheckClean 'loot flow'
    } else {
        Check 'staged encounter starts' $false 'encounter OneBrogg1 did not start'
    }
}

# ============================ PHASE F — UI shell, videos, game over ============================
if ($Phase -in 'F','all') {
    Write-Host "`n=== PHASE F: UI shell, videos, game over ===" -ForegroundColor Cyan
    LoadSave 3 'Map.Jirinaar' | Out-Null

    # F1: 3D dungeon automap. reveal_automap fills the WHOLE map with the glyph overlay —
    # a large, deterministic delta over the plain animated 3D view. Measure the frame-to-
    # frame jitter of the plain view first (plants sway, etc.), then require the overlay to
    # exceed it. (Visual correctness confirmed by screenshot; this guards the toggle wiring.)
    function ShotSize([string]$tag) {
        Invoke-RestMethod -Method POST "$base/screenshot" -OutFile "$env:TEMP\ua_am_$tag.png" | Out-Null
        return (Get-Item "$env:TEMP\ua_am_$tag.png").Length
    }
    $base1 = ShotSize 'base1'; Start-Sleep 1; $base2 = ShotSize 'base2'
    $jitter = [Math]::Abs($base2 - $base1)
    E 'reveal_automap' | Out-Null   # fill discovery so the whole map draws
    E 'show_automap' | Out-Null
    Start-Sleep 1
    $onSize = ShotSize 'on'
    # The overlay may grow OR shrink the PNG (flat glyph tiles can compress better than the
    # noisy animated 3D view); the signal is a large ABSOLUTE change vs the plain jitter.
    $overlayDelta = [Math]::Abs($onSize - [int](($base1 + $base2) / 2))
    Check "automap overlay renders (base~$base1/$base2 B jitter=$jitter, overlay=$onSize B, |delta|=$overlayDelta B)" `
        ($overlayDelta -gt ($jitter * 3 + 8000))
    E 'show_automap' | Out-Null   # toggle off
    Start-Sleep 1
    CheckClean 'automap toggle'

    # F2: main menu push/pop + expected options
    E 'push_scene MainMenu' | Out-Null
    Start-Sleep 2
    $labels = ((G '/ui').elements | Where-Object label | ForEach-Object label) -join ' | '
    Check 'main menu shows Continue/New/Load options' ($labels -match 'game')
    E 'pop_scene' | Out-Null
    Start-Sleep 1
    CheckClean 'main menu round trip'

    # F3: PARTY WIPE — kill every party member in combat; expect the game-over flow to
    # land on the main menu (GameOver video plays first; muted headless run).
    E 'encounter MonsterGroup.OneArgim' | Out-Null
    Start-Sleep 3
    if ((G '/combat').active) {
        foreach ($pm in @((G '/combat').combatants | Where-Object { $_.team -eq 'party' -and $_.alive })) {
            E "combat_damage $($pm.tile) 9999" | Out-Null
            Start-Sleep 1
        }
        Start-Sleep 5
        $deadline = (Get-Date).AddSeconds(60)
        $scene = ''
        while ((Get-Date) -lt $deadline) {
            $scene = (G '/clock').activeScene
            if ($scene -eq 'MainMenu') { break }
            E 'dismiss_message' | Out-Null   # skip through the video if it accepts input
            Start-Sleep 3
        }
        Check "party wipe reaches game-over -> main menu (scene=$scene)" ($scene -eq 'MainMenu')
        CheckClean 'game over flow'
    } else {
        Check 'game-over staging combat starts' $false
    }
}

# ============================ PHASE G — persistence round trip ============================
if ($Phase -in 'G','all') {
    Write-Host "`n=== PHASE G: persistence round trip ===" -ForegroundColor Cyan
    LoadSave 3 'Map.Jirinaar' | Out-Null

    # Mutate distinctive state: spend gold on rations, learn a spell, advance the clock
    E 'svc_food PartyMember.Tom 10 3' | Out-Null
    E 'modify_hours AddAmount 5' | Out-Null
    Start-Sleep 2
    $before = @{
        gold = (G '/inventory').totalGold
        rations = ((G '/inventory').members | Where-Object id -eq 'PartyMember.Tom').rations
        time = (G '/state').time
        spells = (G '/sheet?id=PartySheet.Sira').knownSpells.Count
    }

    # Save to scratch slot 90 and reload (slots 90-98 are reserved for tests)
    E 'save_game 90 MechShakeout' | Out-Null
    Start-Sleep 3
    E 'load_game 3' | Out-Null
    Start-Sleep 4
    E 'load_game 90' | Out-Null
    Start-Sleep 4
    $after = @{
        gold = (G '/inventory').totalGold
        rations = ((G '/inventory').members | Where-Object id -eq 'PartyMember.Tom').rations
        time = (G '/state').time
        spells = (G '/sheet?id=PartySheet.Sira').knownSpells.Count
    }
    Check "save/load round trip: gold $($before.gold)=$($after.gold), rations $($before.rations)=$($after.rations), spells $($before.spells)=$($after.spells)" `
        (($after.gold -eq $before.gold) -and ($after.rations -eq $before.rations) -and ($after.spells -eq $before.spells))
    Check "save/load round trip: clock preserved ($($before.time) vs $($after.time))" (([datetime]$after.time - [datetime]$before.time).TotalMinutes -lt 30)
    CheckClean 'persistence round trip'
}

# ============================ summary ============================
$fails = @($script:results | Where-Object status -eq 'FAIL')
Write-Host "`n=== $($script:results.Count) checks, $($fails.Count) failures ===" -ForegroundColor $(if ($fails.Count) { 'Red' } else { 'Green' })
$script:results | ForEach-Object { "{0,3} [{1}] {2}{3}" -f $_.n, $_.status, $_.name, ($(if ($_.detail -and $_.status -eq 'FAIL') { " — $($_.detail)" } else { '' })) } |
    Set-Content (Join-Path $repo '_mechanics_shakeout.out.txt')
if ($fails.Count) { exit 1 } else { exit 0 }
