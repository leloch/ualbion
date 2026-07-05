using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAlbion.Api;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;
using UAlbion.Game.State.Player;
using UAlbion.Game.Text;

namespace UAlbion.Game.State;

public class GameState : GameServiceComponent<IGameState>, IGameState
{
    const int DaysPerMonth = 30;
    const int HoursPerDay = 24;

    readonly SheetApplier _sheetApplier;
    SavedGame _game;
    Party _party;
    CharacterSheet _leader;
    CharacterSheet _subject;
    CharacterSheet _currentInventory;
    CharacterSheet _combatant;
    CharacterSheet _victim;
    ItemData _weapon;

    public ICharacterSheet Leader    => _leader;
    public ICharacterSheet Subject   => _subject;
    public ICharacterSheet CurrentInventory => _currentInventory;
    public ICharacterSheet Combatant => _combatant;
    public ICharacterSheet Victim    => _victim;
    public ItemData Weapon => _weapon;

    public DateTime Time => SavedGame.Epoch + (_game?.ElapsedTime ?? TimeSpan.Zero);
    public int HoursSinceResting => _game?.HoursSinceResting ?? 0;
    public IParty Party => _party;
    public ICharacterSheet GetSheet(SheetId id) => _game.Sheets.TryGetValue(id, out var sheet) ? sheet : null;
    public short GetTicker(TickerId id) => _game.Tickers.TryGetValue(id, out var value) ? value : (short)0;
    public bool GetSwitch(SwitchId id) => _game.GetSwitch(id);
    public IPlayer GetPlayerForCombatPosition(int position) => 
        _party.StatusBarOrder
            .Where((_, i) => _game.CombatPositions[i] == position)
            .FirstOrDefault();

    public int? GetCombatPositionForPlayer(PartyMemberId id)
    {
        var offset = (SavedGame.CombatRows - SavedGame.CombatRowsForParty) * SavedGame.CombatColumns;
        for (int i = 0; i < _party.StatusBarOrder.Count; i++)
            if (_party.StatusBarOrder[i].Id == id)
                return _game.CombatPositions[i] + offset;

        return null;
    }

    public MapChangeCollection TemporaryMapChanges => _game.TemporaryMapChanges;
    public MapChangeCollection PermanentMapChanges => _game.PermanentMapChanges;
    public ActiveItems ActiveItems => _game.ActiveItems;
    public IList<NpcState> Npcs => _game.Npcs;
    public IDictionary<AutomapId, byte[]> Automaps => _game.Automaps;
    public bool IsChainDisabled(MapId mapId, ushort chain) => _game.IsChainDisabled(mapId, chain);
    public bool IsNpcDisabled(MapId mapId, byte npcNum) => _game.IsNpcDisabled(mapId, npcNum);
    public bool IsChestOpen(ChestId id) => _game?.IsChestOpen(id) ?? false;
    public bool IsDoorOpen(DoorId id) => _game?.IsDoorOpen(id) ?? false;

    // Conversation keywords discovered by the party, shared across every NPC so a word learnt
    // from one carries to the next. Runtime store (carries within a session); cross-save
    // persistence pends the decoded save-format word offset.
    readonly HashSet<WordId> _discoveredWords = [];
    public IReadOnlyCollection<WordId> DiscoveredWords => _discoveredWords;
    public void DiscoverWord(WordId word) { if (!word.IsNone) _discoveredWords.Add(word); }
    public bool IsAutomapMarkerFound(int markerId) => _game?.IsAutomapMarkerFound(markerId) ?? false;
    public void SetAutomapMarkerFound(int markerId) => _game?.SetAutomapMarkerFound(markerId, true);

    int PartySlotOf(PartyMemberId member)
    {
        var order = _party?.StatusBarOrder;
        if (order == null)
            return -1;
        for (int i = 0; i < order.Count; i++)
            if (order[i]?.Id == member)
                return i;
        return -1;
    }

    public int GetActiveSpellPct(PartyMemberId member, int type)
        => _game?.GetActiveSpellPct(PartySlotOf(member), type) ?? 0;

    public int GetActiveSpellHours(PartyMemberId member, int type)
        => _game?.GetActiveSpellHours(PartySlotOf(member), type) ?? 0;

    public int AmbientLightSpellPct => _game?.GetAmbientLightPct() ?? 0;
    public bool IsEventUsed(AssetId eventSetId, ActionEvent action) => _game.IsEventUsed(eventSetId, action);

    public MapId MapId => _game.MapId;
    public MapId MapIdForNpcs
    {
        get => _game.MapIdForNpcs;
        set => _game.MapIdForNpcs = value;
    }

    public GameState()
    {
        OnAsync<NewGameEvent>(e => NewGame(e.MapId, e.X, e.Y));
        OnAsync<SyntheticScenarioEvent>(e => BuildSyntheticScenario(e.Name));
        OnAsync<LoadGameEvent>(e => LoadGame(e.Id));
        On<SaveGameEvent>(e => SaveGame(e.Id, e.Name));
        On<FastClockEvent>(e => TickCount += e.Frames);
        On<GetTimeEvent>(_ => Info(Time.ToString("O")));
        On<SetTimeEvent>(e => _game.ElapsedTime = e.Time - SavedGame.Epoch);
        On<EventVisitedEvent>(e => _game?.UseEvent(e.Id, e.Action));
        On<LoadMapEvent>(OnLoadMap);
        On<SwitchEvent>(OnSwitch);
        On<TickerEvent>(OnTicker);
        // word_known map event: mark a keyword discovered so it carries to other NPCs.
        On<WordKnownEvent>(e =>
        {
            // TXT-03: honour the SwitchOperation — Clear removes, Set adds, Toggle flips. The old
            // form treated Set AND Toggle as "add" and Clear as a no-op, so a word could never be
            // un-known.
            if (e.Word.IsNone) return;
            switch (e.Operation)
            {
                case SwitchOperation.Clear: _discoveredWords.Remove(e.Word); break;
                case SwitchOperation.Set: _discoveredWords.Add(e.Word); break;
                case SwitchOperation.Toggle:
                    if (!_discoveredWords.Remove(e.Word)) _discoveredWords.Add(e.Word);
                    break;
            }
        });
        On<ModifyDaysEvent>(OnModifyDays);
        On<ModifyHoursEvent>(OnModifyHours);
        On<ModifyMTicksEvent>(OnModifyMTicks);
        On<TrapEvent>(OnTrap);
        On<RestEvent>(OnRest);
        // pause (map opcode 0x1A, RE _RE_MISC7.md §C): block the chain for Length/60 seconds
        // (the byte is 1/60 s system-timer ticks; 0 = wait for input, approximated as a beat).
        OnAsync<PauseEvent>(e => RaiseA(new WallClockTimerEvent(e.Length == 0 ? 0.5f : e.Length / 60.0f)));
        // clone_automap (0x10): copy one map's automap discovery bytes onto another — used
        // when a map has pre/post-event variants so exploration carries across the swap.
        On<CloneAutomapEvent>(e =>
        {
            if (_game == null || e.From.IsNone || e.To.IsNone)
                return;
            if (_game.Automaps.TryGetValue(new AutomapId(e.From.Id), out var bytes) && bytes != null)
                _game.Automaps[new AutomapId(e.To.Id)] = (byte[])bytes.Clone();
            else
                Info($"[CloneAutomap] no discovery data for {e.From}, nothing to copy to {e.To}");
        });
        OnAsync<PartyWaitEvent>(OnWait);
        On<HourElapsedEvent>(_ => ProcessHourElapsed());
        On<ResetFatigueEvent>(_ => { if (_game != null) _game.HoursSinceResting = 0; }); // Recuperation = magical full rest
        On<AddActiveSpellEvent>(e =>
        {
            // Write-once while empty (fcn.000607da) — re-casts on an active entry do nothing.
            _game?.TryAddActiveSpell(PartySlotOf(e.MemberId), e.EntryType, e.Hours, e.Percent);
        });
        On<AddAmbientLightSpellEvent>(e =>
        {
            // The Light spell's ambient entry ACCUMULATES (fcn.0006085d).
            _game?.AddAmbientLight(e.Hours, e.Percent);
            Raise(new DungeonLightChangedEvent());
        });
        On<SetSpecialItemActiveEvent>(ActivateItem);
        On<EventChainOffEvent>(e => _game.SetChainDisabled(e.Map, e.ChainNumber, SetFlag(e.Operation, _game.IsChainDisabled(e.Map, e.ChainNumber))));
        On<ModifyNpcOffEvent>(e => _game.SetNpcDisabled(e.Map, e.NpcNum, SetFlag(e.Operation, _game.IsNpcDisabled(e.Map, e.NpcNum))));
        On<NpcOffEvent>(e => _game.SetNpcDisabled(MapId.None, e.NpcNum, true));
        On<NpcOnEvent>(e => _game.SetNpcDisabled(MapId.None, e.NpcNum, false));
        // execute (RE _RE_OPCODES_FLAGS.md, 0x3b78f): enable/disable the active NPC — Unk1!=0
        // enables, ==0 disables. Best-effort: resolve the NPC index from the event source's
        // AssetId (the chain's NPC). Direct call (not Raise) since Raise skips our own handler.
        On<ExecuteEvent>(OnExecute);
        On<SetChestOpenEvent>(e => _game.SetChestOpen(e.Chest, SetFlag(e.Operation, _game.IsChestOpen(e.Chest))));
        On<SetDoorOpenEvent>(e => _game.SetDoorOpen(e.Door, SetFlag(e.Operation, _game.IsDoorOpen(e.Door))));
        // The data-change family is a set of sibling classes (one per ChangeProperty kind),
        // not subclasses of DataChangeEvent — each needs its own subscription or script
        // events like change_status / change_attribute silently do nothing.
        On<DataChangeEvent>(OnDataChange);
        On<ChangeStatusEvent>(OnDataChange);
        On<ChangeItemEvent>(OnDataChange);
        On<ChangeAttributeEvent>(OnDataChange);
        On<ChangeSkillEvent>(OnDataChange);
        On<ChangeLanguageEvent>(OnDataChange);
        On<ChangeEventSetEvent>(OnDataChange);
        On<ChangeWordSetEvent>(OnDataChange);
        On<ChangeSpellsEvent>(OnDataChange);
        On<SetContextEvent>(OnSetContext);

        AttachChild(new InventoryManager(GetWriteableInventory, GetItem));
        _sheetApplier = AttachChild(new SheetApplier());
        AttachChild(new StatusConditionTicker());
        AttachChild(new UAlbion.Game.Audio.AmbientSoundManager());

        // Populate the spell-effect registry. Currently only Dji-Kas heal-status spells (16..19)
        // are implemented — every other spell still falls through to the registry's "Failed"
        // default which makes the caster spend AP without an effect (matches the original
        // engine's no-op behaviour better than crashing). Add new schools here as they're
        // reverse-engineered (see _RE_COMBAT.md → Phase 3.x).
        UAlbion.Game.Combat.SpellEffectRegistry.Clear();
        UAlbion.Game.Combat.Spells.DjiKasSpells.RegisterAll();
        UAlbion.Game.Combat.Spells.DjiKantosSpells.RegisterAll();
        UAlbion.Game.Combat.Spells.DruidSpells.RegisterAll();
        UAlbion.Game.Combat.Spells.OquloKamulosSpells.RegisterAll();
        UAlbion.Game.Combat.Spells.ZombieMagicSpells.RegisterAll();
    }

    void OnLoadMap(LoadMapEvent e)
    {
        if (_game != null)
            _game.MapId = e.MapId;
    }

    void OnSwitch(SwitchEvent e)
    {
        _game.SetSwitch(e.SwitchId, e.Operation switch
        {
            SwitchOperation.Clear => false,
            SwitchOperation.Set => true,
            SwitchOperation.Toggle => !_game.GetSwitch(e.SwitchId),
            _ => false
        });
    }

    void OnTicker(TickerEvent e)
    {
        _game.Tickers.TryGetValue(e.TickerId, out var curValue);
        _game.Tickers[e.TickerId] = (byte)e.Operation.Apply(curValue, e.Amount, 0, 255);
    }

    // World trap tile (RE _RE_OPCODES_WORLD.md, handler 0x3aa35). Per affected member: a gender
    // filter (Unk2 bitmask), a Luck save that avoids the trap entirely, then on failure inflict
    // condition Unk1 (bit index, >=12 = none) and RandomVary(Unk6) LP damage. Unconscious skipped.
    void OnExecute(ExecuteEvent e)
    {
        if (Context is not EventContext c || c.Source == null || _game?.Npcs == null)
            return;
        var npcId = c.Source.AssetId;
        int index = -1;
        for (int i = 0; i < _game.Npcs.Length; i++)
            if (_game.Npcs[i] != null && _game.Npcs[i].Id == npcId) { index = i; break; }
        if (index < 0)
            return; // not NPC-sourced (e.g. tile-triggered) — can't resolve the active NPC
        _game.SetNpcDisabled(MapId.None, (byte)index, e.Unk1 == 0); // Unk1==0 → disable, else enable
    }

    void OnTrap(TrapEvent e)
    {
        var party = TryResolve<IParty>();
        var rng = TryResolve<IRandom>();
        if (party == null || rng == null)
            return;

        IEnumerable<IPlayer> targets = e.Unk3 switch
        {
            1 => party.StatusBarOrder,                                       // whole party
            2 => party.StatusBarOrder.Where((_, i) => i == e.Unk5),          // specific slot
            _ => party.Leader == null ? [] : new[] { party.Leader }          // leader / active
        };

        foreach (var member in targets)
        {
            var combat = member?.Effective?.Combat;
            if (combat == null)
                continue;
            if ((e.Unk2 & (1 << (int)member.Effective.Gender)) == 0)         // gender filter
                continue;
            if ((combat.Conditions & PlayerConditions.Unconscious) != 0)     // already down
                continue;

            int luck = member.Effective.Attributes?.Luck?.Current ?? 0;
            if (UAlbion.Game.Combat.DamageCalculator.PercentRoll(luck, rng.Generate(100)))
                continue; // lucky escape

            var target = new TargetId(AssetType.PartyMember, member.Id.Id);
            if (e.Unk1 < 12)
                Raise(new ChangeStatusEvent(target, (PlayerCondition)e.Unk1, NumericOperation.SetToMaximum));
            if (e.Unk6 > 0)
            {
                int dmg = UAlbion.Game.Combat.DamageCalculator.VaryDamage(e.Unk6, rng.Generate(51));
                if (dmg > 0)
                    Raise(new DataChangeEvent(target, ChangeProperty.Health, NumericOperation.SubtractAmount, (ushort)Math.Min(ushort.MaxValue, dmg)));
            }
        }
    }

    void OnModifyDays(ModifyDaysEvent e) // Only SetAmount + AddAmount
    {
        switch (e.Operation)
        {
            case NumericOperation.SetAmount:
            {
                var time = _game.ElapsedTime;
                var dayOfMonth = e.Amount % DaysPerMonth;
                int month = time.Days / DaysPerMonth;
                int newDays = month * DaysPerMonth + dayOfMonth;
                _game.ElapsedTime = new TimeSpan(newDays, time.Hours, time.Minutes, time.Seconds, time.Milliseconds);
                break;
            }
            case NumericOperation.AddAmount:
                // Was `for (i < Amount) AdvanceTimeInHours(Amount * HoursPerDay)` — advanced
                // Amount² days instead of Amount.
                AdvanceTimeInHours(e.Amount * HoursPerDay);
                break;
        }
    }

    void OnModifyHours(ModifyHoursEvent e) // Only SetAmount + AddAmount
    {
        switch (e.Operation)
        {
            case NumericOperation.SetAmount:
            {
                var time = _game.ElapsedTime;
                _game.ElapsedTime = new TimeSpan(time.Days, e.Amount % HoursPerDay, time.Minutes, time.Seconds);
                break;
            }
            case NumericOperation.AddAmount:
                // AdvanceTimeInHours already adds the hours — the extra ElapsedTime add here
                // doubled every time advance (rest for 8h moved the clock 16h).
                AdvanceTimeInHours(e.Amount);
                break;
        }
    }

    void OnModifyMTicks(ModifyMTicksEvent e) // Only SetAmount + AddAmount
    {
        switch (e.Operation)
        {
            case NumericOperation.SetAmount:
            {
                var time = _game.ElapsedTime;
                var minutes = e.Amount * 60.0f / 48;
                _game.ElapsedTime = new TimeSpan(time.Days, time.Hours, 0, 0) + TimeSpan.FromMinutes(minutes);
                break;
            }
            case NumericOperation.AddAmount:
            {
                var minutes = e.Amount * 60.0f / 48;
                _game.ElapsedTime += TimeSpan.FromMinutes(minutes);
                // AdvanceTimeInTicks(e.Amount);
                break;
            }
        }
    }

    void OnSetContext(SetContextEvent e)
    {
        var state = Resolve<IGameState>();

        var asset = e.AssetId.Type switch
        {
            AssetType.PartyMember => (object)state.GetSheet(((PartyMemberId)e.AssetId).ToSheet()),
            AssetType.PartySheet => state.GetSheet(e.AssetId),
            AssetType.NpcSheet => state.GetSheet(e.AssetId),
            AssetType.MonsterSheet => Assets.LoadSheet(e.AssetId),
            AssetType.Item => Assets.LoadItem(e.AssetId),
            _ => null
        };

        switch (e.Type)
        {
            case ContextType.Leader:
                _leader = (CharacterSheet)asset;
                break;
            case ContextType.Subject:
                _subject = (CharacterSheet)asset;
                break;
            case ContextType.Inventory:
                _currentInventory = (CharacterSheet)asset;
                break;
            case ContextType.Combatant:
                _combatant = (CharacterSheet)asset;
                break;
            case ContextType.Victim:
                _victim = (CharacterSheet)asset;
                break;
            case ContextType.Weapon:
                _weapon = (ItemData)asset;
                break;
        }
    }

    void AdvanceTimeInHours(int hours)
    {
        // The original advances bulk time (rest, wait, inn stays) through the normal
        // hour tick (fcn.000439b3), so the per-hour processing — EveryHour map event
        // chains, the poison drain, the fatigue counter, dungeon light decay — runs for
        // every hour skipped. GameClock only detects hour crossings from incremental
        // frame time, so we fire the events here. (No double-fire: GameClock re-reads
        // state.Time each frame, so it never sees the jump as a crossing.)
        for (int i = 0; i < hours; i++)
        {
            var before = Time;
            _game.ElapsedTime += TimeSpan.FromHours(1);
            Raise(HourElapsedEvent.Instance);
            // STATE-01: EventExchange.Raise skips the sender's OWN subscriptions, so GameState's
            // per-hour handler (fatigue + spell-duration decay) never ran for bulk-advanced hours.
            // Invoke it directly here; the Raise above still drives the other components.
            ProcessHourElapsed();
            if (Time.Date != before.Date)
                Raise(DayElapsedEvent.Instance);
        }
    }

    void ProcessHourElapsed()
    {
        if (_game == null) return;
        if (_game.HoursSinceResting < ushort.MaxValue) _game.HoursSinceResting++; // fatigue clock (rest resets it)
        _game.TickActiveSpells(); // shield/light entries decay hourly (fcn.000605ed)
        BurnTorches();
    }

    // Torch burn (RE _RE_MISC7.md §D, fcn.00049584): once per hour, decrement the charge byte
    // of every equipped AND backpack LightSource (type 0x16) on all members; 0xFF = infinite
    // (skip); when a torch reaches 0 it is destroyed. Recompute dungeon light after.
    void BurnTorches()
    {
        var assets = TryResolve<UAlbion.Formats.IAssetManager>();
        if (assets == null) return;

        bool anyBurnt = false;
        foreach (var member in _party.StatusBarOrder)
        {
            var sheet = member == null ? null : GetSheet(member.Id.ToSheet()) as CharacterSheet;
            var inv = sheet?.Inventory;
            if (inv == null) continue;

            foreach (var slot in inv.EnumerateAll())
            {
                if (slot == null || slot.Item.Type != AssetType.Item)
                    continue;
                var item = assets.LoadItem(slot.Item);
                if (item?.TypeId != ItemType.LightSource)
                    continue;
                // 0xFF = eternal flame (RE quirk: never depletes). 0 = an unlit/untracked torch
                // (the remake doesn't seed a lifetime on pickup) — leave it, don't destroy it.
                if (slot.Charges is 0 or 0xFF)
                    continue;

                slot.Charges--;
                anyBurnt = true;
                if (slot.Charges == 0)
                {
                    Info($"[Torch] {slot.Item} burnt out for {member.Id}");
                    slot.Clear();
                }
            }
        }

        if (anyBurnt)
            Raise(new DungeonLightChangedEvent());
    }

    static bool SetFlag(SwitchOperation operation, bool value) =>
        operation switch
        {
            SwitchOperation.Clear => false,
            SwitchOperation.Set => true,
            SwitchOperation.Toggle => !value,
            _ => value
        };

    CharacterSheet GetTarget(TargetId id)
    {
        switch (id.Type)
        {
            // PartyMember ids must be remapped to PartySheet — Sheets is keyed by SheetId
            // and constructing a SheetId from a PartyMember-typed AssetId throws.
            case AssetType.PartyMember:
                return _game.Sheets.TryGetValue(new PartyMemberId(id.Id).ToSheet(), out var member) ? member : null;

            case AssetType.NpcSheet:
                return _game.Sheets.TryGetValue((AssetId)id, out var target) ? target : null;

            case AssetType.Target:
            {
                if (id == Base.Target.Leader) return _leader;
                if (id == Base.Target.Inventory) return _currentInventory;
                if (id == Base.Target.Attacker) return _combatant;
                if (id == Base.Target.Target) return _victim;
                if (id == Base.Target.Subject) return _subject;
                return null;

            }
            default: return null;
        }
    }

    void OnDataChange(IDataChangeEvent e)
    {
        if (e.Target == Base.Target.Everyone)
        {
            foreach (var member in _party.StatusBarOrder.Select(x => _game.Sheets[x.Id.ToSheet()]))
                _sheetApplier.Apply(e, member);
            return;
        }

        var target = GetTarget(e.Target);
        if (target == null)
        {
            Warn($"Could not resolve target {e.Target} when executing \"{e}\"");
            return;
        }

        _sheetApplier.Apply(e, target);
    }


    public IInventory GetInventory(InventoryId id)
    {
        if (id.Type == InventoryType.Player)
        {
            var player = _party[id.ToAssetId()];
            if (player != null)
                return player.Apparent.Inventory;
        }

        return GetWriteableInventory(id);
    }

    Inventory GetWriteableInventory(InventoryId id)
    {
        Inventory inventory;
        switch(id.Type)
        {
            case InventoryType.Player:
                inventory = _game.Sheets.TryGetValue(id.ToSheetId(), out var member) ? member.Inventory : null;
                break;
            case InventoryType.Chest: _game.Inventories.TryGetValue(id.ToAssetId(), out inventory); break;
            case InventoryType.Merchant: _game.Inventories.TryGetValue(id.ToAssetId(), out inventory); break;
            default:
                throw new InvalidOperationException($"Unexpected inventory type requested: \"{id.Type}\"");
        }

        return inventory;
    }

    ItemData GetItem(ItemId id) => Assets.LoadItemStrict(id);

    public int TickCount { get; private set; }
    public bool Loaded => _game != null;

    AlbionTask NewGame(MapId mapId, ushort x, ushort y)
    {
        Raise(new ReloadAssetsEvent()); // Make sure we don't end up with cached assets from the last game.
        _game = new SavedGame
        {
            MapId = mapId,
            ElapsedTime = TimeSpan.FromHours(8), // Game starts at 8:00 AM
            PartyX = x,
            PartyY = y,
            PartyDirection = Direction.East,
            ActiveMembers = { [0] = Base.PartyMember.Tom },
            CombatPositions = { [0] = 1 } // Tom starts off in the second position
        };

        LoadAllSheetsAndInventories(_game);
        return InitialiseGame();
    }

    // Load every party/NPC sheet and chest/merchant inventory into the save (shared by NewGame
    // and synthetic-scenario construction). The full sheet set is always present; ActiveMembers
    // just selects which become the live party.
    void LoadAllSheetsAndInventories(SavedGame game)
    {
        var assets = Assets;
        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.PartySheet))
            game.Sheets.Add(id, assets.LoadSheet(id));

        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.NpcSheet))
            game.Sheets.Add(id, assets.LoadSheet(id));

        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.Chest))
            game.Inventories.Add(id, assets.LoadInventory(id));

        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.Merchant))
            game.Inventories.Add(id, assets.LoadInventory(id));
    }

    // Build an arbitrary game state from a SyntheticScenario (a parameterised NewGame) — the
    // save-independent "warp anywhere" path. Reuses InitialiseGame (the same path load_game uses),
    // so any map (2D/3D) loads cleanly; the only crash vector is the spawn tile, handled by the
    // post-init re-validation against the live collider.
    AlbionTask BuildSyntheticScenario(string name)
    {
        if (!SyntheticScenarioLibrary.TryGet(name, out var spec))
        {
            Error($"[synth] unknown scenario '{name}'");
            return AlbionTask.CompletedTask;
        }
        return BuildSyntheticScenarioCore(spec);
    }

    async AlbionTask BuildSyntheticScenarioCore(SyntheticScenario spec)
    {
        Raise(new ReloadAssetsEvent());
        _game = new SavedGame
        {
            MapId = spec.Map,
            ElapsedTime = TimeSpan.FromHours(spec.TimeHours),
            PartyDirection = spec.Direction,
            PartyX = spec.X ?? 1,
            PartyY = spec.Y ?? 1,
        };

        if (spec.Party == null || spec.Party.Count == 0)
        {
            _game.ActiveMembers[0] = Base.PartyMember.Tom;
            _game.CombatPositions[0] = 1;
        }
        else
        {
            for (int i = 0; i < spec.Party.Count && i < SavedGame.MaxPartySize; i++)
                _game.ActiveMembers[i] = spec.Party[i];
        }

        LoadAllSheetsAndInventories(_game);

        // Seed persisted flags before InitialiseGame.
        foreach (var s in spec.SwitchesToSet) _game.SetSwitch(s, true);
        foreach (var c in spec.ChestsOpen) _game.SetChestOpen(c, true);
        foreach (var d in spec.DoorsOpen) _game.SetDoorOpen(d, true);
        foreach (var n in spec.NpcsDisabled) _game.SetNpcDisabled(n.Map, n.Npc, true);

        // Spells + gold on the loaded sheets.
        var leader = PartyMemberId.None;
        foreach (var member in _game.ActiveMembers)
        {
            if (member.IsNone) continue;
            if (leader.IsNone) leader = member;
            if (_game.Sheets.TryGetValue(member.ToSheet(), out var sheet) && sheet.Magic?.KnownSpells != null)
                foreach (var spell in spec.KnownSpells)
                    if (!sheet.Magic.KnownSpells.Contains(spell))
                        sheet.Magic.KnownSpells.Add(spell);
        }
        if (spec.Gold > 0 && !leader.IsNone
            && _game.Sheets.TryGetValue(leader.ToSheet(), out var ls) && ls.Inventory?.Gold != null)
            ls.Inventory.Gold.Amount = spec.Gold;

        await InitialiseGame();

        // Re-validate the spawn against the LIVE collider and re-jump before the next frame, so an
        // unknown/stale coord can't strand the party in a wall (the crash we saw with raw teleport).
        var collision = TryResolve<ICollisionManager>();
        if (SpawnValidator.TryResolve(collision, _game.PartyX, _game.PartyY, out var vx, out var vy)
            && (vx != _game.PartyX || vy != _game.PartyY))
        {
            _game.PartyX = (ushort)vx;
            _game.PartyY = (ushort)vy;
            Raise(new PartyJumpEvent((ushort)vx, (ushort)vy));
            Raise(new CameraJumpEvent((ushort)vx, (ushort)vy));
            Raise(new PlayerEnteredTileEvent((ushort)vx, (ushort)vy));
        }

        // Words are runtime-only state (not in SavedGame) — seed them after init.
        foreach (var w in spec.WordsKnown)
            Raise(new WordKnownEvent(SwitchOperation.Set, w));

        Info($"[synth] '{spec.Name}' -> map {_game.MapId} ({_game.PartyX},{_game.PartyY})");
    }

    string IdToPath(ushort id)
    {
        var pathResolver = Resolve<IPathResolver>();
        // TODO: This path currently exists in two places: here and Game\Gui\Menus\PickSaveSlot.cs
        return pathResolver.ResolvePath($"$(SAVES)/SAVE.{id:D3}");
    }

    AlbionTask LoadGame(ushort id)
    {
        _game = Assets.LoadSavedGame(IdToPath(id));
        if (_game == null)
            return AlbionTask.CompletedTask;

        LoadDiscoveredWords(IdToPath(id));
        return InitialiseGame();
    }

    // Discovered conversation words are runtime-only in the original (the savegame has no field for
    // them — confirmed by RE in _RE_WORD_SAVE.md). To keep word-gated quests from breaking across a
    // save/reload (the revival goal), we persist them in a SIDECAR file next to the save — a
    // deliberate QoL deviation with ZERO risk to the vanilla save format / round-trip (the 13 stock
    // saves simply have no sidecar). One numeric WordId per line.
    void LoadDiscoveredWords(string savePath)
    {
        _discoveredWords.Clear();
        try
        {
            var disk = Resolve<IFileSystem>();
            var path = savePath + ".words";
            if (!disk.FileExists(path))
                return;
            foreach (var line in disk.ReadAllLines(path))
                if (int.TryParse(line.Trim(), out var wid) && wid > 0)
                    _discoveredWords.Add(new WordId(AssetType.Word, wid));
        }
        catch (Exception ex) { Error($"[words] sidecar read failed: {ex.Message}"); }
    }

    void SaveDiscoveredWords(IFileSystem disk, string savePath)
    {
        try
        {
            var path = savePath + ".words";
            if (_discoveredWords.Count > 0)
                disk.WriteAllText(path, string.Join("\n", _discoveredWords.Select(w => w.Id)));
            else if (disk.FileExists(path))
                disk.DeleteFile(path);
        }
        catch (Exception ex) { Error($"[words] sidecar write failed: {ex.Message}"); }
    }

    /// <summary>
    /// Any active hostile monster party on the current map blocks resting and waiting
    /// (the original's global 0x15cc5a, "It's too dangerous here", SYSTEXTS 601).
    /// </summary>
    bool HostileMonstersOnMap()
    {
        if (_game?.Npcs == null)
            return false;
        for (int i = 0; i < _game.Npcs.Length; i++)
        {
            var npc = _game.Npcs[i];
            if (npc == null || npc.Id.Type != AssetType.MonsterGroup)
                continue;
            if (_game.IsNpcDisabled(MapId.None, (byte)i))
                continue;
            return true;
        }
        return false;
    }

    void OnRest(RestEvent e)
    {
        if (_game == null || _party == null)
            return;

        var tf = Resolve<ITextFormatter>();

        // RE'd rest gating (_RE_COMBAT.md "Placeholder formulas" item 3): ALL the gates
        // live in the map-menu popup builder (0x2204e), not the executor — map RestMode
        // (mapFlags & 0xC), active hostile monsters ("too dangerous", 601) and the
        // 3-hour fatigue check ("nobody is tired", 603) only apply to the map-menu rest
        // (hours == 0). Explicit-hour rests (inn SleepInRoom, scripts) run the executor
        // directly like the original's 0x68b05.
        var restMode = TryResolve<IMapManager>()?.Current?.MapData?.RestMode
                       ?? UAlbion.Formats.Assets.Maps.RestMode.RestEightHours;
        bool explicitHours = e.Hours > 0;
        if (!explicitHours)
        {
            if (restMode is UAlbion.Formats.Assets.Maps.RestMode.Wait
                         or UAlbion.Formats.Assets.Maps.RestMode.NoResting)
            {
                Info($"Resting is not available here (RestMode {restMode})");
                return;
            }

            if (HostileMonstersOnMap())
            {
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.MapPopup_ItsTooDangerousHere)));
                return;
            }

            if (_game.HoursSinceResting < 3)
            {
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Rest_NobodyInThePartyIsTired)));
                return;
            }
        }

        // Duration (executor 0x68b05): dungeons — and daytime hours [4, 19) anywhere —
        // rest a flat 8 hours (msg 605); otherwise rest till dawn, landing on 07:00
        // (msg 604). An explicit hour count (inn) is used as-is.
        int hours = e.Hours;
        if (!explicitHours)
        {
            int hour = Time.Hour;
            if (restMode == UAlbion.Formats.Assets.Maps.RestMode.RestEightHours || (hour >= 4 && hour < 19))
            {
                hours = 8;
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Rest_ThePartyRestsForEightHours)));
            }
            else
            {
                hours = hour < 4 ? 7 - hour : 31 - hour;
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Rest_ThePartyRestsTillDawn)));
            }
        }

        // Recovery is applied BEFORE the clock advance (RE 5C: the executor heals first,
        // then fcn.000439b3 ticks the hours — poison etc drain from the healed totals).
        // NOTE: handler methods are called directly rather than Raise() — the exchange
        // skips a sender's own subscriptions, and GameState owns all of them.
        foreach (var member in _party.StatusBarOrder)
        {
            if (member == null) continue;
            var target = member.Id;
            var sheet = GetSheet(member.Id.ToSheet());
            if (sheet == null) continue;

            // Exhaustion cured FIRST (executor order) so the recovery below uses the
            // restored Stamina/MagicTalent values, like the original.
            OnDataChange(new ChangeStatusEvent(target, PlayerCondition.Exhausted, NumericOperation.SetToMinimum));

            // 2 rations per member; a member with no food doesn't recover.
            if (sheet.Inventory?.Rations == null || sheet.Inventory.Rations.Amount < 2)
            {
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Rest_XCannotRecuperateHeHasNoFoodLeft, sheet.GetName(ReadVar(V.User.Gameplay.Language)))));
                continue;
            }
            OnDataChange(new DataChangeEvent(target, ChangeProperty.Food, NumericOperation.SubtractAmount, 2));

            int lpGain = (sheet.Combat?.LifePoints?.Max ?? 0) / 2 + (sheet.Attributes?.Stamina?.Current ?? 0) / 15;
            int spGain = (sheet.Magic?.SpellPoints?.Max ?? 0) / 2 + (sheet.Attributes?.MagicTalent?.Current ?? 0) / 15;

            if (lpGain > 0)
                OnDataChange(new DataChangeEvent(target, ChangeProperty.Health, NumericOperation.AddAmount, (ushort)Math.Min(ushort.MaxValue, lpGain)));
            if (spGain > 0)
                OnDataChange(new DataChangeEvent(target, ChangeProperty.Mana, NumericOperation.AddAmount, (ushort)Math.Min(ushort.MaxValue, spGain)));
        }

        // STATE-01: zero the fatigue counter BEFORE advancing the clock. AdvanceTimeInHours
        // raises HourElapsedEvent per hour, and StatusConditionTicker reads HoursSinceResting;
        // if the stale pre-rest value (>48) is still set during the rest's own hour ticks it
        // re-applies Exhausted and drains LP, undoing the cure/heal above. The original zeroes
        // on both sides, so we keep the post-advance reset too (counter ends at 0).
        _game.HoursSinceResting = 0;
        OnModifyHours(new ModifyHoursEvent(NumericOperation.AddAmount, (ushort)hours));
        _game.HoursSinceResting = 0;

        // PartySleeps (action 0x3D): the original fires each active party member's PartySleeps
        // chain on every rest — e.g. Sira2 (NPC 984) heals Sira+Mellthas, runs do_script
        // Script.60, and drives the Sira/Mellthas romance + Triifalai-seed beats (B1′). Only
        // members whose event set actually has the chain react.
        foreach (var member in _party.StatusBarOrder)
        {
            var setId = member == null ? default : GetSheet(member.Id.ToSheet())?.EventSetId ?? default;
            if (setId.IsNone) continue;
            var set = Assets.LoadEventSet(setId);
            var chain = FindActionChainIndex(set, ActionType.PartySleeps);
            if (chain != null)
                Raise(new TriggerChainEvent(set, chain.Value, new EventSource(set.Id, UAlbion.Formats.Assets.Maps.TriggerType.Action)));
        }

        Info($"The party rests for {hours} hours.");
    }

    // First chain in an event set headed by the given action type (block 0, no argument).
    static ushort? FindActionChainIndex(UAlbion.Formats.Assets.EventSet set, ActionType type)
    {
        if (set?.Chains == null) return null;
        foreach (var idx in set.Chains)
            if (idx < set.Events.Count && set.Events[idx].Event is ActionEvent a
                && a.ActionType == type && a.Block == 0 && a.Argument.IsNone)
                return idx;
        return null;
    }

    /// <summary>
    /// City "Wait" (RestMode 0): prompt for an hour count (SYSTEXTS 724) and advance the
    /// clock — no recovery, no ration cost. Blocked by active hostile monsters, the same
    /// flag that gates Rest (popup builder 0x2204e).
    /// </summary>
    async AlbionTask OnWait(PartyWaitEvent _)
    {
        if (_game == null)
            return;

        if (HostileMonstersOnMap())
        {
            var tf = Resolve<ITextFormatter>();
            Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.MapPopup_ItsTooDangerousHere)));
            return;
        }

        int hours = await RaiseQueryA(new NumericPromptEvent(Base.SystemText.MapPopup_WaitForHowManyHours, 0, 23));
        if (hours <= 0 || _game == null)
            return;

        OnModifyHours(new ModifyHoursEvent(NumericOperation.AddAmount, (ushort)hours));
        Info($"The party waits for {hours} hours.");
    }

    void SaveGame(ushort id, string name)
    {
        if (_game == null)
            return;

        var disk = Resolve<IFileSystem>();
        var spellManager = Resolve<ISpellManager>();
        _game.Name = name;

        for (int i = 0; i < SavedGame.MaxPartySize; i++)
            _game.ActiveMembers[i] = _party.StatusBarOrder.Count > i
                ? _party.StatusBarOrder[i].Id
                : PartyMemberId.None;

        // Sync the live party position into the header — it's only read on load, so
        // without this the save records wherever the party was when the game started.
        var leader = _party?.Leader;
        if (leader != null)
        {
            var pos = leader.GetPosition();
            var map = TryResolve<IMapManager>()?.Current;
            _game.PartyX = (ushort)pos.X;
            // 2D maps put the tile Y in pos.Y; 3D maps use pos.Z (Y is the camera height).
            _game.PartyY = (ushort)(map?.MapType == UAlbion.Formats.Assets.Maps.MapType.ThreeD ? pos.Z : pos.Y);

            // 3D facing wasn't being persisted (always saved the default), so reloading a dungeon
            // restored the wrong direction. Derive it from the camera's quantised yaw (the
            // grid-step model keeps it on a quadrant): N=0 E=90 S=180 W=270 → Direction 0..3.
            if (map?.MapType == UAlbion.Formats.Assets.Maps.MapType.ThreeD)
            {
                var cam = TryResolve<UAlbion.Core.Visual.ICamera>();
                if (cam != null)
                {
                    int q = ((int)MathF.Round(cam.Yaw / (MathF.PI / 2f)) % 4 + 4) % 4;
                    _game.PartyDirection = (UAlbion.Formats.Direction)q;
                }
            }
        }

        // var key = new AssetId(AssetType.SavedGame, id);
        // Serialize NPC sprites with the LIVE map's gfx type — 3D NPCs are ObjectGroup and must NOT
        // go through the SpriteId path (which throws). Defaults to TwoD if no map is loaded.
        var npcMapType = TryResolve<IMapManager>()?.Current?.MapType ?? UAlbion.Formats.Assets.Maps.MapType.TwoD;
        using var stream = disk.OpenWriteTruncate(IdToPath(id));
        using var aw = AlbionSerdes.CreateWriter(stream);
        // Must use the same mapping the loader uses (ModApplier loads saves with
        // AssetMapping.Global) — an empty mapping throws "Type X is not currently mapped"
        // on the first id conversion and leaves a truncated save file behind.
        if (Environment.GetEnvironmentVariable("UALBION_ANNOTATE_SAVE") == "1")
        {
            // Debug aid: write a field-by-field annotation alongside the save so writer-side
            // offsets can be diffed against the reader's annotation (DumpSave 'a' command).
            using var annotationStream = disk.OpenWriteTruncate(IdToPath(id) + ".write.txt");
            using var annotationWriter = new System.IO.StreamWriter(annotationStream);
            using var annotated = new SerdesNet.AnnotationProxySerdes(aw, annotationWriter);
            SavedGame.Serdes(_game, AssetMapping.Global, annotated, spellManager, npcMapType);
        }
        else
        {
            SavedGame.Serdes(_game, AssetMapping.Global, aw, spellManager, npcMapType);
        }

        SaveDiscoveredWords(disk, IdToPath(id));
    }

    async AlbionTask InitialiseGame()
    {
        // Defensive: never dereference a null _game (a failed/abandoned load). LoadGame already
        // guards the null return, but other entry points (synthetic scenarios) call us too.
        if (_game == null)
        {
            Error("InitialiseGame called with no loaded game state - aborting");
            return;
        }

        _party?.Remove();
        _party = AttachChild(new Party(_game.Sheets, GetWriteableInventory, _game.CombatPositions));

        foreach (var member in _game.ActiveMembers)
            if (!member.IsNone)
                _party.AddMember(member);

        await RaiseA(new LoadMapEvent(_game.MapId));

        Raise(new StartClockEvent());
        Raise(new SetPartyLeaderEvent(_party.Leader.Id, 0, 0));
        Raise(new PartyChangedEvent());
        Raise(new PartyJumpEvent(_game.PartyX, _game.PartyY));
        Raise(new CameraJumpEvent(_game.PartyX, _game.PartyY));
        Raise(new PartyTurnEvent(_game.PartyDirection));
        Raise(new PlayerEnteredTileEvent(_game.PartyX, _game.PartyY));
    }

    void ActivateItem(SetSpecialItemActiveEvent e)
    {
        if (e.IsActive)
        {
            if (e.Item == Base.Item.Clock) _game.ActiveItems |= ActiveItems.Clock;
            else if (e.Item == Base.Item.Compass) _game.ActiveItems |= ActiveItems.Compass;
            else if (e.Item == Base.Item.MonsterEye) _game.ActiveItems |= ActiveItems.MonsterEye;
        }
        else
        {
            if (e.Item == Base.Item.Clock) _game.ActiveItems &= ~ActiveItems.Clock;
            else if (e.Item == Base.Item.Compass) _game.ActiveItems &= ~ActiveItems.Compass;
            else if (e.Item == Base.Item.MonsterEye) _game.ActiveItems &= ~ActiveItems.MonsterEye;
        }
    }
}
