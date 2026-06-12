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
        OnAsync<LoadGameEvent>(e => LoadGame(e.Id));
        On<SaveGameEvent>(e => SaveGame(e.Id, e.Name));
        On<FastClockEvent>(e => TickCount += e.Frames);
        On<GetTimeEvent>(_ => Info(Time.ToString("O")));
        On<SetTimeEvent>(e => _game.ElapsedTime = e.Time - SavedGame.Epoch);
        On<EventVisitedEvent>(e => _game?.UseEvent(e.Id, e.Action));
        On<LoadMapEvent>(OnLoadMap);
        On<SwitchEvent>(OnSwitch);
        On<TickerEvent>(OnTicker);
        On<ModifyDaysEvent>(OnModifyDays);
        On<ModifyHoursEvent>(OnModifyHours);
        On<ModifyMTicksEvent>(OnModifyMTicks);
        On<RestEvent>(OnRest);
        OnAsync<PartyWaitEvent>(OnWait);
        On<HourElapsedEvent>(_ => { if (_game != null && _game.HoursSinceResting < ushort.MaxValue) _game.HoursSinceResting++; }); // fatigue clock (rest resets it)
        On<ResetFatigueEvent>(_ => { if (_game != null) _game.HoursSinceResting = 0; }); // Recuperation = magical full rest
        On<SetSpecialItemActiveEvent>(ActivateItem);
        On<EventChainOffEvent>(e => _game.SetChainDisabled(e.Map, e.ChainNumber, SetFlag(e.Operation, _game.IsChainDisabled(e.Map, e.ChainNumber))));
        On<ModifyNpcOffEvent>(e => _game.SetNpcDisabled(e.Map, e.NpcNum, SetFlag(e.Operation, _game.IsNpcDisabled(e.Map, e.NpcNum))));
        On<NpcOffEvent>(e => _game.SetNpcDisabled(MapId.None, e.NpcNum, true));
        On<NpcOnEvent>(e => _game.SetNpcDisabled(MapId.None, e.NpcNum, false));
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
            if (Time.Date != before.Date)
                Raise(DayElapsedEvent.Instance);
        }
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

        var assets = Assets;
        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.PartySheet))
            _game.Sheets.Add(id, assets.LoadSheet(id));

        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.NpcSheet))
            _game.Sheets.Add(id, assets.LoadSheet(id));

        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.Chest))
            _game.Inventories.Add(id, assets.LoadInventory(id));

        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.Merchant))
            _game.Inventories.Add(id, assets.LoadInventory(id));

        return InitialiseGame();
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

        return InitialiseGame();
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

        // NOTE: handler methods are called directly rather than Raise() — the exchange
        // skips a sender's own subscriptions, and GameState owns all of them.
        OnModifyHours(new ModifyHoursEvent(NumericOperation.AddAmount, (ushort)hours));
        _game.HoursSinceResting = 0;

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

        Info($"The party rests for {hours} hours.");
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
        }

        // var key = new AssetId(AssetType.SavedGame, id);
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
            SavedGame.Serdes(_game, AssetMapping.Global, annotated, spellManager);
        }
        else
        {
            SavedGame.Serdes(_game, AssetMapping.Global, aw, spellManager);
        }
    }

    async AlbionTask InitialiseGame()
    {
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
