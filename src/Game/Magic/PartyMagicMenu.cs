using System.Collections.Generic;
using System.Linq;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Combat;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.State;
using UAlbion.Game.Text;

namespace UAlbion.Game.Magic;

/// <summary>Opens the out-of-combat spell menu for a party member (portrait right-click → Use magic).</summary>
[Event("magic_menu", "Open the out-of-combat spell menu for a party member")]
public record ShowMagicMenuEvent([property: EventPart("member")] PartyMemberId MemberId) : EventRecord;

/// <summary>Casts a known spell outside combat. Target defaults to the caster.</summary>
[Event("cast_spell", "Cast a known spell outside combat")]
public record CastPartySpellEvent(
    [property: EventPart("member")] PartyMemberId MemberId,
    [property: EventPart("spell")] SpellId SpellId,
    [property: EventPart("target")] PartyMemberId TargetId) : EventRecord;

/// <summary>
/// Casts a magic item's spell outside combat through the same field-cast machinery as
/// cast_spell (so heals/buffs get the real ApplyHeal/sheet hooks), but with the item rules:
/// flat mastery 50, no SP cost, no mastery growth, and the SLOT's charge consumed
/// (RE 5B: cast core fcn.0005fdf7 returns 0x32 for item slots).
/// </summary>
public record CastPartyItemSpellEvent(
    PartyMemberId MemberId,
    SpellId SpellId,
    PartyMemberId TargetId,
    UAlbion.Formats.Assets.Inv.InventorySlotId ItemSlot) : EventRecord;

/// <summary>
/// Out-of-combat spell casting: lists the member's known spells that are castable in the
/// current environment (SpellData.Environments vs the map type), resolves them through
/// the same SpellEffectRegistry as combat with the RE'd mastery multiplier, and deducts
/// SP. Party-targeting spells (heals etc) show a second menu to pick the target member.
/// </summary>
public class PartyMagicMenu : GameComponent
{
    public PartyMagicMenu()
    {
        On<ShowMagicMenuEvent>(e => ShowSpells(e.MemberId));
        On<ShowMagicTargetMenuEvent>(ShowTargetMenu);
        On<CastPartySpellEvent>(e => CastInner(e.MemberId, e.SpellId, e.TargetId, null));
        On<CastPartyItemSpellEvent>(e => CastInner(e.MemberId, e.SpellId, e.TargetId, e.ItemSlot));
        On<UAlbion.Game.Events.ShowTeleporterMenuEvent>(_ => ShowTeleporterMenu());
    }

    /// <summary>
    /// The Teleporter spell's destination picker: the current 3D map's automap markers
    /// (goto-points), limited to ones the party has VISITED — the original gates by
    /// switch(7, MarkerId), set when stepping on the marker tile (fcn.0005c10a).
    /// Choosing one jumps the party there.
    /// </summary>
    void ShowTeleporterMenu()
    {
        var map = TryResolve<IMapManager>()?.Current?.MapData as UAlbion.Formats.Assets.Maps.MapData3D;
        if (map == null || map.Automap.Count == 0)
        {
            Info("[Magic] Teleporter: no destinations on this map");
            return;
        }

        var state = TryResolve<IGameState>();
        var mapId = TryResolve<IMapManager>()?.Current?.MapId ?? MapId.None;
        var options = new List<ContextMenuOption>();
        foreach (var marker in map.Automap)
        {
            if (marker == null)
                continue;
            if (state != null && !state.IsAutomapMarkerFound(marker.MarkerId))
                continue; // not visited yet
            var label = string.IsNullOrWhiteSpace(marker.Name) ? $"({marker.X}, {marker.Y})" : marker.Name;
            options.Add(new ContextMenuOption(
                new LiteralText(label),
                // Same-map teleport: MapManager handles PartyJump (2D) + CameraJump (3D).
                new UAlbion.Formats.MapEvents.TeleportEvent(mapId, marker.X, marker.Y, UAlbion.Formats.Direction.Unchanged, 1, 2),
                ContextMenuGroup.Actions));
        }

        if (options.Count == 0)
        {
            Info("[Magic] Teleporter: no visited destinations on this map yet");
            return;
        }

        ShowMenu(new LiteralText("Teleport where?"), options);
    }

    SpellEnvironments CurrentEnvironment()
    {
        // RE 5B fcn.00060554: env index = (mapFlags & 0xC) >> 2 == (int)RestMode (out of
        // combat — this menu is the non-combat cast path). 0 City / 1 Dungeon / 2 Wilderness
        // / 3 Interior. Previously gated on MapType, which hid dungeon-only utility spells.
        var rest = TryResolve<IMapManager>()?.Current?.MapData?.RestMode ?? RestMode.RestEightHours;
        return (SpellEnvironments)(1 << (int)rest);
    }

    void ShowSpells(PartyMemberId memberId)
    {
        var sheet = Resolve<IGameState>().GetSheet(memberId.ToSheet());
        if (sheet == null)
            return;

        var tf = Resolve<ITextFormatter>();
        var env = CurrentEnvironment();
        var options = new List<ContextMenuOption>();

        foreach (var spellId in sheet.Magic.KnownSpells)
        {
            var spell = Assets.LoadSpell(spellId);
            if (spell == null)
                continue;
            if ((spell.Environments & env) == 0)
                continue; // not castable here (e.g. combat-only)

            var name = spell.Name.IsNone
                ? (IText)new LiteralText(spellId.ToString())
                : tf.Center().NoWrap().Format((TextId)spell.Name);

            // Party-targeting spells pick a member next; everything else self-casts.
            bool needsTarget = (spell.Targets & (SpellTargets.Party | SpellTargets.DeadParty)) != 0;
            IEvent next = needsTarget
                ? new ShowMagicTargetMenuEvent(memberId, spellId)
                : new CastPartySpellEvent(memberId, spellId, memberId);

            options.Add(new ContextMenuOption(name, next, ContextMenuGroup.Actions));
        }

        if (options.Count == 0)
            return;

        ShowMenu(tf.Center().NoWrap().Fat().Format(Base.SystemText.PartyPopup_UseMagic), options);
    }

    /// <summary>Second step for party-targeting spells: pick the target member. When
    /// ItemSlot is set the pick fires an ITEM cast (wand rules) instead of a spellbook cast.</summary>
    public record ShowMagicTargetMenuEvent(
        PartyMemberId MemberId,
        SpellId SpellId,
        UAlbion.Formats.Assets.Inv.InventorySlotId? ItemSlot = null) : EventRecord;

    void ShowTargetMenu(ShowMagicTargetMenuEvent e)
    {
        var party = Resolve<IParty>();
        var tf = Resolve<ITextFormatter>();
        var language = ReadVar(V.User.Gameplay.Language);
        var options = new List<ContextMenuOption>();

        foreach (var member in party.StatusBarOrder)
        {
            var name = new LiteralText(member.Apparent.GetName(language));
            IEvent cast = e.ItemSlot is { } slot
                ? new CastPartyItemSpellEvent(e.MemberId, e.SpellId, member.Id, slot)
                : new CastPartySpellEvent(e.MemberId, e.SpellId, member.Id);
            options.Add(new ContextMenuOption(name, cast, ContextMenuGroup.Actions));
        }

        ShowMenu(tf.Center().NoWrap().Fat().Format(Base.SystemText.PartyPopup_UseMagic), options);
    }

    void ShowMenu(IText heading, List<ContextMenuOption> options)
    {
        var window = Resolve<UAlbion.Core.IGameWindow>();
        var cursorManager = Resolve<Input.ICursorManager>();
        var uiPosition = window.PixelToUi(cursorManager.Position);
        Raise(new ContextMenuEvent(uiPosition, heading, options));
    }

    // Shared field-cast core. itemSlot == null → a spellbook cast (SP cost, mastery growth);
    // itemSlot set → a magic-item cast (flat M=50, no SP, no mastery; the slot's charge is
    // consumed afterwards — RE 5B item-cast rules).
    void CastInner(PartyMemberId memberId, SpellId spellId, PartyMemberId targetId, UAlbion.Formats.Assets.Inv.InventorySlotId? itemSlot)
    {
        // Party members already implement ICombatParticipant with a computed Effective
        // sheet — reuse them directly.
        var party = Resolve<IParty>();
        IPlayer caster = null, target = null;
        foreach (var member in party.StatusBarOrder)
        {
            if (member.Id == memberId) caster = member;
            if (member.Id == targetId) target = member;
        }
        target ??= caster;
        if (caster == null)
            return;

        var spell = Assets.LoadSpell(spellId);
        int cost = itemSlot == null ? spell?.Cost ?? 0 : 0; // item casts are SP-free
        int sp = caster.Effective?.Magic?.SpellPoints?.Current ?? 0;
        if (cost > 0 && sp < cost)
        {
            Info($"[Magic] {memberId} lacks SP for {spellId} ({sp}/{cost})");
            return;
        }

        // Same RE'd mastery multiplier as combat casts; items run at the flat 50.
        int m;
        if (itemSlot != null)
            m = 50;
        else
        {
            ushort mastery = 0;
            caster.Effective?.Magic?.SpellStrengths?.TryGetValue(spellId, out mastery);
            m = System.Math.Max(1, (mastery + 50) / 100);
        }

        var rng = Resolve<IRandom>();
        var context = new SpellCastContext
        {
            Caster = caster,
            Target = target,
            CombatTargetPosition = -1,
            SpellStrength = (byte)(caster.Effective?.Level ?? 1),
            MasteryMultiplier = m,
            Random = max => rng.Generate(max),
            RaiseEvent = Raise,
            ApplyDamage = (p, amount) => ChangeHealth(p, amount, NumericOperation.SubtractAmount),
            ApplyHeal = (p, amount) => ChangeHealth(p, amount, NumericOperation.AddAmount),
            GetAllies = () => party.StatusBarOrder.Cast<ICombatParticipant>().ToList(),
            HoursAwake = () => TryResolve<IGameState>()?.HoursSinceResting ?? 0,
            GetActiveSpellPct = (p, type) => p?.SheetId.Type == AssetType.PartySheet
                ? TryResolve<IGameState>()?.GetActiveSpellPct(new PartyMemberId(AssetType.PartyMember, p.SheetId.Id), type) ?? 0
                : 0,
        };

        var outcome = SpellEffectRegistry.Cast(spellId, context);
        Info($"[Magic] {memberId} casts {spellId} on {targetId}{(itemSlot != null ? " (item)" : "")}: {outcome}");

        // Item casts consume the slot's charge on every attempt (the original charges the
        // item even for unfulfilled effects), then skip SP/mastery entirely.
        if (itemSlot != null)
        {
            Raise(new UAlbion.Game.Events.Inventory.ConsumeItemSlotChargeEvent(itemSlot.Value));
            if (outcome != SpellCastOutcome.Failed)
                foreach (var sample in CombatAudio.GetCastSamples(spellId))
                    Raise(new SoundEffectEvent(sample, 100, 0, 0, 0, SoundMode.GlobalOneShot));
            return;
        }

        if (outcome == SpellCastOutcome.Failed)
            return;

        if (cost > 0)
            Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, memberId.Id), ChangeProperty.Mana, NumericOperation.SubtractAmount, (ushort)cost));

        // Mastery grows with use (RE post-cast fcn.000603ae: mastery += MagicTalent, cap
        // 10000) — persisted on the caster's base sheet, same as the combat cast path.
        var baseSheet = TryResolve<IGameState>()?.GetSheet(memberId.ToSheet());
        var strengths = baseSheet?.Magic?.SpellStrengths;
        if (strengths != null)
        {
            int talent = baseSheet.Attributes?.MagicTalent?.Current ?? 0;
            if (talent > 0)
            {
                strengths.TryGetValue(spellId, out var current);
                strengths[spellId] = Combat.CombatFormulas.GrowMastery(current, talent);
            }
        }

        // Cast SFX — same RE'd per-spell sample table the combat path uses.
        foreach (var sample in CombatAudio.GetCastSamples(spellId))
            Raise(new SoundEffectEvent(sample, 100, 0, 0, 0, SoundMode.GlobalOneShot));
    }

    void ChangeHealth(ICombatParticipant p, int amount, NumericOperation op)
    {
        if (p?.SheetId == null || amount <= 0 || p.SheetId.Type != AssetType.PartySheet)
            return;
        var targetId = new TargetId(AssetType.PartyMember, p.SheetId.Id);
        Raise(new DataChangeEvent(targetId, ChangeProperty.Health, op, (ushort)System.Math.Min(ushort.MaxValue, amount)));
    }

}
