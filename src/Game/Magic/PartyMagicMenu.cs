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
        On<CastPartySpellEvent>(Cast);
        On<UAlbion.Game.Events.ShowTeleporterMenuEvent>(_ => ShowTeleporterMenu());
    }

    /// <summary>
    /// The Teleporter spell's destination picker: the current 3D map's automap markers
    /// (goto-points). Choosing one jumps the party there. PLACEHOLDER: all markers are
    /// offered — the original limits the list to places already visited.
    /// </summary>
    void ShowTeleporterMenu()
    {
        var map = TryResolve<IMapManager>()?.Current?.MapData as UAlbion.Formats.Assets.Maps.MapData3D;
        if (map == null || map.Automap.Count == 0)
        {
            Info("[Magic] Teleporter: no destinations on this map");
            return;
        }

        var mapId = TryResolve<IMapManager>()?.Current?.MapId ?? MapId.None;
        var options = new List<ContextMenuOption>();
        foreach (var marker in map.Automap)
        {
            if (marker == null)
                continue;
            var label = string.IsNullOrWhiteSpace(marker.Name) ? $"({marker.X}, {marker.Y})" : marker.Name;
            options.Add(new ContextMenuOption(
                new LiteralText(label),
                // Same-map teleport: MapManager handles PartyJump (2D) + CameraJump (3D).
                new UAlbion.Formats.MapEvents.TeleportEvent(mapId, marker.X, marker.Y, UAlbion.Formats.Direction.Unchanged, 1, 2),
                ContextMenuGroup.Actions));
        }

        ShowMenu(new LiteralText("Teleport where?"), options);
    }

    SpellEnvironments CurrentEnvironment()
    {
        var map = TryResolve<IMapManager>()?.Current;
        return map?.MapData?.MapType switch
        {
            MapType.ThreeD => SpellEnvironments.Dungeon,
            MapType.TwoDOutdoors => SpellEnvironments.Outdoors,
            _ => SpellEnvironments.Indoors,
        };
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

    /// <summary>Second step for party-targeting spells: pick the target member.</summary>
    public record ShowMagicTargetMenuEvent(PartyMemberId MemberId, SpellId SpellId) : EventRecord;

    void ShowTargetMenu(ShowMagicTargetMenuEvent e)
    {
        var party = Resolve<IParty>();
        var tf = Resolve<ITextFormatter>();
        var language = ReadVar(V.User.Gameplay.Language);
        var options = new List<ContextMenuOption>();

        foreach (var member in party.StatusBarOrder)
        {
            var name = new LiteralText(member.Apparent.GetName(language));
            options.Add(new ContextMenuOption(name, new CastPartySpellEvent(e.MemberId, e.SpellId, member.Id), ContextMenuGroup.Actions));
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

    void Cast(CastPartySpellEvent e)
    {
        // Party members already implement ICombatParticipant with a computed Effective
        // sheet — reuse them directly.
        var party = Resolve<IParty>();
        IPlayer caster = null, target = null;
        foreach (var member in party.StatusBarOrder)
        {
            if (member.Id == e.MemberId) caster = member;
            if (member.Id == e.TargetId) target = member;
        }
        target ??= caster;
        if (caster == null)
            return;

        var spell = Assets.LoadSpell(e.SpellId);
        int cost = spell?.Cost ?? 0;
        int sp = caster.Effective?.Magic?.SpellPoints?.Current ?? 0;
        if (cost > 0 && sp < cost)
        {
            Info($"[Magic] {e.MemberId} lacks SP for {e.SpellId} ({sp}/{cost})");
            return;
        }

        // Same RE'd mastery multiplier as combat casts.
        ushort mastery = 0;
        caster.Effective?.Magic?.SpellStrengths?.TryGetValue(e.SpellId, out mastery);
        int m = System.Math.Max(1, (mastery + 50) / 100);

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
        };

        var outcome = SpellEffectRegistry.Cast(e.SpellId, context);
        Info($"[Magic] {e.MemberId} casts {e.SpellId} on {e.TargetId}: {outcome}");

        if (outcome == SpellCastOutcome.Failed)
            return;

        if (cost > 0)
            Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, e.MemberId.Id), ChangeProperty.Mana, NumericOperation.SubtractAmount, (ushort)cost));

        // Cast SFX — same RE'd per-spell sample table the combat path uses.
        foreach (var sample in CombatAudio.GetCastSamples(e.SpellId))
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
