using System;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.State;
using UAlbion.Game.Events;
using UAlbion.Game.Events.Inventory;

namespace UAlbion.Game.State;

public class SheetApplier : Component
{
    public void Apply(IDataChangeEvent e, CharacterSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(sheet);

        switch (e)
        {
            case ChangeEventSetEvent esetEvent: sheet.EventSetId = esetEvent.EventSet; break;
            case ChangeWordSetEvent wordEvent:  sheet.WordSetId  = wordEvent.WordSet; break;
            case ChangeAttributeEvent attribEvent:  ApplyAttribute(sheet, attribEvent); break;
            case ChangeSkillEvent skillEvent:       ApplySkill(sheet, skillEvent); break;
            case ChangeLanguageEvent languageEvent: ApplyLanguage(sheet, languageEvent); break;
            case ChangeStatusEvent statusEvent:     ApplyStatus(sheet, statusEvent); break;
            case ChangeItemEvent itemEvent:         ApplyItem(sheet, itemEvent); break;
            case ChangeSpellsEvent spellsEvent:     ApplySpells(sheet, spellsEvent); break;
            case DataChangeEvent generic:           ApplyGeneric(sheet, generic); break;
            default: throw new ArgumentOutOfRangeException(nameof(e));
        }

        Raise(new InventoryChangedEvent(new InventoryId(sheet.Id)));
        Raise(new SheetChangedEvent(sheet.Id));
    }

    void ApplyGeneric(CharacterSheet sheet, DataChangeEvent generic)
    {
        var amount = generic.IsRandom
            ? (ushort)Resolve<IRandom>().Generate(generic.Amount)
            : generic.Amount;

        switch (generic.ChangeProperty)
        {
            case ChangeProperty.Health:
                sheet.Combat.LifePoints.Apply(generic.Operation, amount);
                LifeChecks(sheet);
                break;

            case ChangeProperty.Mana:
                sheet.Magic.SpellPoints.Apply(generic.Operation, amount);
                break;

            case ChangeProperty.MaxHealth:
                sheet.Combat.LifePoints.ApplyToMax(generic.Operation, amount);
                LifeChecks(sheet);
                break;

            case ChangeProperty.MaxMana:
                sheet.Magic.SpellPoints.ApplyToMax(generic.Operation, amount);
                break;

            case ChangeProperty.Experience:
                sheet.Combat.ExperiencePoints = generic.Operation.Apply(sheet.Combat.ExperiencePoints, amount);
                ExperienceChecks(sheet);
                break;

            case ChangeProperty.TrainingPoints:
                sheet.Combat.TrainingPoints = generic.Operation.Apply16(sheet.Combat.TrainingPoints, amount);
                break;
            case ChangeProperty.Gold:
                sheet.Inventory.Gold.Amount = generic.Operation.Apply16(sheet.Inventory.Gold.Amount, amount);
                break;
            case ChangeProperty.Food:
                sheet.Inventory.Rations.Amount = generic.Operation.Apply16(sheet.Inventory.Rations.Amount, amount);
                break;

            case ChangeProperty.Unused4:
            case ChangeProperty.Unused6:
            case ChangeProperty.UnusedA:
            case ChangeProperty.UnusedB:
            case ChangeProperty.UnusedE:
            case ChangeProperty.UnusedF:
                // These ChangeProperty values are intentionally unused in the original engine;
                // surface them as Info so they don't show up as scary warnings in transcripts.
                Info($"Skipping unused ChangeProperty {generic.ChangeProperty}");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(generic), $"Event was of unexpected generic type {generic.ChangeProperty}");
        }
    }

    void ApplySpells(CharacterSheet sheet, ChangeSpellsEvent spellsEvent)
    {
        var spellManager = Resolve<ISpellManager>();
        var spellId = spellManager.GetSpellId(spellsEvent.School, (byte)spellsEvent.SpellNumber);
        var known = sheet.Magic.KnownSpells.Contains(spellId);

        switch (known)
        {
            case true when spellsEvent.Operation is NumericOperation.SetToMinimum or NumericOperation.Toggle:
                sheet.Magic.KnownSpells.Remove(spellId);
                break;
            case false when spellsEvent.Operation is NumericOperation.SetToMaximum or NumericOperation.Toggle:
                sheet.Magic.KnownSpells.Add(spellId);
                // Learning seeds the spell's mastery at 4 × MagicTalent (RE batch 4,
                // the LearnSpells service handler) when no strength exists yet.
                if (!sheet.Magic.SpellStrengths.ContainsKey(spellId))
                {
                    int talent = sheet.Attributes?.MagicTalent?.Current ?? 0;
                    sheet.Magic.SpellStrengths[spellId] = (ushort)Math.Min(ushort.MaxValue, 4 * talent);
                }
                break;
        }
    }

    void ApplyItem(CharacterSheet sheet, ChangeItemEvent itemEvent)
    {
        // Route an item-change Map event into the inventory pipeline for this party member.
        // Add ⇒ TryGiveItems, Remove ⇒ TryTakeItems. Unsupported operations are logged so any
        // future regression surfaces in transcripts rather than silently dropping.
        var invManager = TryResolve<UAlbion.Game.State.Player.IInventoryManager>();
        if (invManager == null)
            return;

        var amount = itemEvent.IsRandom
            ? (ushort)Resolve<IRandom>().Generate(itemEvent.Amount == 0 ? (ushort)1 : itemEvent.Amount)
            : itemEvent.Amount;
        if (amount == 0) amount = 1;

        var inventoryId = new InventoryId(sheet.Id);
        switch (itemEvent.Operation)
        {
            case NumericOperation.AddAmount:
            case NumericOperation.AddPercentage:
                invManager.TryGiveItems(inventoryId, new ItemSlot(default) { Item = itemEvent.ItemId, Amount = amount }, amount);
                break;
            case NumericOperation.SubtractAmount:
            case NumericOperation.SubtractPercentage:
                invManager.TryTakeItems(inventoryId, null, itemEvent.ItemId, amount);
                break;
            default:
                Info($"ChangeItemEvent {itemEvent.Operation} on {itemEvent.ItemId} not yet handled");
                break;
        }
    }

    static void ApplyStatus(CharacterSheet sheet, ChangeStatusEvent statusEvent)
    {
        var condition = statusEvent.Status.ToFlag();
        var before = sheet.Combat.Conditions;
        // A condition is a single flag, so the amount-based operations collapse to
        // set/clear. Previously Add/SubtractAmount fell through to a silent no-op,
        // which made every condition-cure (and several inflictions) do nothing.
        sheet.Combat.Conditions = statusEvent.Operation switch
        {
            NumericOperation.SetToMaximum  => before | condition,
            NumericOperation.AddAmount     => before | condition,
            NumericOperation.SetToMinimum  => before & ~condition,
            NumericOperation.SubtractAmount => before & ~condition,
            NumericOperation.Toggle => before ^ condition,
            _ => before
        };

        // Exhaustion carries stat penalties that apply on set and revert on cure —
        // hooked here so every path (48 h fatigue, rest, Recuperation) gets them.
        if (statusEvent.Status == PlayerCondition.Exhausted)
        {
            bool was = (before & PlayerConditions.Exhausted) != 0;
            bool now = (sheet.Combat.Conditions & PlayerConditions.Exhausted) != 0;
            if (!was && now)
                ApplyExhaustionPenalties(sheet);
            else if (was && !now)
                RestoreExhaustionBackups(sheet);
        }
    }

    /// <summary>
    /// Exhaustion penalties, RE'd from MAIN.EXE fcn.00039362 (the odd-hour fatigue check):
    /// the current attribute/skill values are backed up (the stat record's +6 word — the
    /// CharacterAttribute.Backup field) and then reduced: Strength ×3/4, every other
    /// attribute ×1/2, all four skills ×1/2.
    /// </summary>
    static void ApplyExhaustionPenalties(CharacterSheet sheet)
    {
        static void Penalise(CharacterAttribute attr, int num, int den)
        {
            if (attr == null || attr.Backup != 0)
                return; // already penalised — don't stack
            attr.Backup = attr.Current;
            attr.Current = (ushort)(attr.Current * num / den);
        }

        Penalise(sheet.Attributes.Strength, 3, 4);
        Penalise(sheet.Attributes.Intelligence, 1, 2);
        Penalise(sheet.Attributes.Dexterity, 1, 2);
        Penalise(sheet.Attributes.Speed, 1, 2);
        Penalise(sheet.Attributes.Stamina, 1, 2);
        Penalise(sheet.Attributes.Luck, 1, 2);
        Penalise(sheet.Attributes.MagicResistance, 1, 2);
        Penalise(sheet.Attributes.MagicTalent, 1, 2);
        Penalise(sheet.Skills.CloseCombat, 1, 2);
        Penalise(sheet.Skills.RangedCombat, 1, 2);
        Penalise(sheet.Skills.CriticalChance, 1, 2);
        Penalise(sheet.Skills.LockPicking, 1, 2);
    }

    /// <summary>
    /// Cure-side restore, RE'd from MAIN.EXE fcn.00037958: attribute/skill currents come
    /// back from the +6 backups (rest and Recuperation both route through here).
    /// </summary>
    static void RestoreExhaustionBackups(CharacterSheet sheet)
    {
        static void Restore(CharacterAttribute attr)
        {
            if (attr == null || attr.Backup == 0)
                return;
            attr.Current = attr.Backup;
            attr.Backup = 0;
        }

        Restore(sheet.Attributes.Strength);
        Restore(sheet.Attributes.Intelligence);
        Restore(sheet.Attributes.Dexterity);
        Restore(sheet.Attributes.Speed);
        Restore(sheet.Attributes.Stamina);
        Restore(sheet.Attributes.Luck);
        Restore(sheet.Attributes.MagicResistance);
        Restore(sheet.Attributes.MagicTalent);
        Restore(sheet.Skills.CloseCombat);
        Restore(sheet.Skills.RangedCombat);
        Restore(sheet.Skills.CriticalChance);
        Restore(sheet.Skills.LockPicking);
    }

    static void ApplyLanguage(CharacterSheet sheet, ChangeLanguageEvent languageEvent)
    {
        var lang = languageEvent.Language.ToFlag();
        sheet.Languages = languageEvent.Operation switch
        {
            NumericOperation.SetToMaximum => sheet.Languages | lang,
            NumericOperation.SetToMinimum => sheet.Languages & ~lang,
            NumericOperation.Toggle => sheet.Languages ^ lang,
            _ => sheet.Languages
        };
    }

    void ApplyAttribute(CharacterSheet sheet, ChangeAttributeEvent attribEvent)
    {
        var attrib = attribEvent.Attribute switch
        {
            PhysicalAttribute.Strength => sheet.Attributes.Strength,
            PhysicalAttribute.Intelligence => sheet.Attributes.Intelligence,
            PhysicalAttribute.Dexterity => sheet.Attributes.Dexterity,
            PhysicalAttribute.Speed => sheet.Attributes.Speed,
            PhysicalAttribute.Stamina => sheet.Attributes.Stamina,
            PhysicalAttribute.Luck => sheet.Attributes.Luck,
            PhysicalAttribute.MagicResistance => sheet.Attributes.MagicResistance,
            PhysicalAttribute.MagicTalent => sheet.Attributes.MagicTalent,
            _ => throw new ArgumentException($"Unknown attribute {attribEvent.Attribute} in event {attribEvent}", nameof(attribEvent))
        };

        var amount = attribEvent.IsRandom
            ? (ushort)Resolve<IRandom>().Generate(attribEvent.Amount)
            : attribEvent.Amount;

        attrib.Apply(attribEvent.Operation, amount);
        Raise(new AttributeChangedEvent(sheet.Id, attribEvent.Attribute));
    }

    void ApplySkill(CharacterSheet sheet, ChangeSkillEvent skillEvent)
    {
        var skill = skillEvent.Skill switch
        {
            Skill.Melee => sheet.Skills.CloseCombat,
            Skill.Ranged => sheet.Skills.RangedCombat,
            Skill.CriticalChance => sheet.Skills.CriticalChance,
            Skill.LockPicking => sheet.Skills.LockPicking,
            _ => throw new ArgumentException($"Unknown skill {skillEvent.Skill} in event {skillEvent}", nameof(skillEvent))
        };

        var amount = skillEvent.IsRandom
            ? (ushort)Resolve<IRandom>().Generate(skillEvent.Amount)
            : skillEvent.Amount;

        skill.Apply(skillEvent.Operation, amount);
        Raise(new SkillChangedEvent(sheet.Id, skillEvent.Skill));
    }

    void LifeChecks(CharacterSheet sheet)
    {
        var lp = sheet.Combat.LifePoints;
        // Clamp Current down when Max has been reduced (level-down, curse, etc).
        // The previous condition was inverted (Max > Current → set Current = Max)
        // which silently undid every Health-loss event by restoring full HP.
        if (lp.Current > lp.Max)
            lp.Current = lp.Max;

        if (lp.Current > 0)
        {
            // Healing above zero wakes an unconscious character — without this, members
            // knocked out in combat would stay down forever after being healed.
            if ((sheet.Combat.Conditions & PlayerConditions.Unconscious) != 0)
            {
                sheet.Combat.Conditions &= ~PlayerConditions.Unconscious;
                Info($"{sheet.Id} regains consciousness ({lp.Current}/{lp.Max} LP)");
            }
            return;
        }

        // At zero HP the original game considers the character Unconscious — a recoverable
        // state distinct from PermanentlyDead. UnconsciousMask in PlayerConditions is what
        // the rest of the game (e.g. Querier line 42, party-effective checks) reads to decide
        // whether a member can act, so flipping that bit is the load-bearing change.
        if ((sheet.Combat.Conditions & PlayerConditions.Unconscious) == 0)
            sheet.Combat.Conditions |= PlayerConditions.Unconscious;

        Raise(new DeathEvent(sheet.Id));

        // If the dead member was the party leader, hand the torch to the first conscious
        // walk-order member so the camera/status bar don't end up tied to an unconscious body.
        // PartyMemberId → SheetId must go through ToSheet() (PartyMember.N → PartySheet.N);
        // the raw AssetId cast throws ArgumentOutOfRange (SheetId rejects PartyMember ids).
        var party = TryResolve<IParty>();
        if (party?.Leader != null && party.Leader.Id.ToSheet() == sheet.Id)
        {
            foreach (var candidate in party.WalkOrder)
            {
                if (candidate.Id.ToSheet() == sheet.Id)
                    continue;
                if ((candidate.Apparent?.Combat?.Conditions & PlayerConditions.UnconsciousMask) != 0)
                    continue;
                Raise(new SetPartyLeaderEvent(candidate.Id, 0, 0));
                return;
            }
        }
    }

    /// <summary>
    /// Level-up check, fired after any XP change. Walks the level curve while the
    /// accumulated XP exceeds the threshold for the next level, applying per-level stat
    /// gains for each step (so a multi-level XP grant levels the character up multiple
    /// times in one shot — matches the original engine's behaviour at hand-in NPCs).
    /// </summary>
    /// <remarks>
    /// RE'd XP curve (see _RE_COMBAT.md "Placeholder formulas"): XP required to reach
    /// level N+1 = max(1, ⌊1.25·N²⌋ + N − 14) × classMultiplier, level cap 50. Class
    /// multipliers: Pilot 25, Scientist 35, IskaiWarrior 30, DjiKasMage 25, Druid 25,
    /// EnlightenedOne 20, Technician 40, (class 7 unused: 0), OquloKamulos 25, Warrior 35.
    /// </remarks>
    void ExperienceChecks(CharacterSheet sheet)
    {
        const int MaxLevel = 50; // the original's level cap
        while (sheet.Level < MaxLevel && sheet.Combat.ExperiencePoints >= XpForNextLevel(sheet.Level, sheet.PlayerClass))
        {
            sheet.Level++;
            ApplyPerLevelGains(sheet);
            Raise(new SheetChangedEvent(sheet.Id));
            Info($"{sheet.Id} levelled up to {sheet.Level} (XP {sheet.Combat.ExperiencePoints})");
        }
    }

    static readonly int[] ClassXpMultipliers = [25, 35, 30, 25, 25, 20, 40, 0, 25, 35];

    internal static int XpForNextLevel(int currentLevel, PlayerClass playerClass)
    {
        int classMul = (int)playerClass < ClassXpMultipliers.Length ? ClassXpMultipliers[(int)playerClass] : 25;
        if (classMul == 0)
            classMul = 25; // defensive: unused class slot / monsters
        int baseXp = Math.Max(1, (int)(1.25 * currentLevel * currentLevel) + currentLevel - 14);
        return baseXp * classMul;
    }

    static void ApplyPerLevelGains(CharacterSheet sheet)
    {
        // LifePointsPerLevel / SpellPointsPerLevel / TrainingPointsPerLevel are encoded
        // in the sheet binary at fixed offsets — see CharacterSheet.cs:105-107. Read them
        // and apply to current and max. These ARE engine-encoded so trustworthy.
        // Null guards: non-caster members have no SpellPoints attribute, and malformed
        // sheets can lack LifePoints — a level-up must never crash the round resolution.
        var hpGain = sheet.LifePointsPerLevel;
        if (hpGain > 0 && sheet.Combat?.LifePoints != null)
        {
            sheet.Combat.LifePoints.ApplyToMax(NumericOperation.AddAmount, hpGain);
            sheet.Combat.LifePoints.Apply(NumericOperation.AddAmount, hpGain);
        }

        // MaxSP per level = INT/30 + SpellPointsPerLevel (RE ApplyLevelUp: MaxSP =
        // level·(EffStat(INT)/30 + w[0xE6]); additive-per-level here is equivalent). The
        // INT/30 term was previously omitted. Casters only (SpellPoints attribute present).
        if (sheet.Magic?.SpellPoints != null)
        {
            int intBonus = (sheet.Attributes?.Intelligence?.Current ?? 0) / 30;
            int spGain = sheet.SpellPointsPerLevel + intBonus;
            if (spGain > 0)
            {
                sheet.Magic.SpellPoints.ApplyToMax(NumericOperation.AddAmount, (ushort)spGain);
                sheet.Magic.SpellPoints.Apply(NumericOperation.AddAmount, (ushort)spGain);
            }
        }

        // Action points (strikes/round) are recomputed from the level on every level-up:
        // clamp(level / LevelsPerActionPoint, 1, 4). Previously never granted — AP stayed at
        // the sheet's starting value, so multi-strike never scaled (RE fcn.00037c22, +0x11).
        if (sheet.Combat != null)
            sheet.Combat.ActionPoints =
                (byte)UAlbion.Game.Combat.CombatFormulas.ActionPointsForLevel(sheet.Level, sheet.LevelsPerActionPoint);

        // Training-point grant per level (this IS 1:1, not an approximation — RE'd in
        // _RE_FIDELITY2.md). The original has NO separate "spell-learning point" pool: the single
        // pool at sheet+0x16 (= Combat.TrainingPoints here) is fed by w[+0xEA] per level
        // (TrainingPointsPerLevel) and is spent ONLY on skill training; spell-learning costs GOLD
        // only. The +0xE8 SpellLearningPointsPerLevel field is dead data the engine never reads.
        var tpGain = sheet.TrainingPointsPerLevel;
        if (tpGain > 0)
            sheet.Combat.TrainingPoints = (ushort)System.Math.Min(ushort.MaxValue, sheet.Combat.TrainingPoints + tpGain);
    }
}
