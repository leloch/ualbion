using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.State;
using UAlbion.Game.Text;

namespace UAlbion.Game;

/// <summary>
/// Handles the NPC service events (PlaceActionEvent) fired from conversation chains —
/// previously these had NO handler, so trainers, healers, taverns and food vendors were
/// silent no-ops. All 12 dispatcher types are now implemented (RE batch 4 decoded the
/// dispatcher fcn.000666e9 + table 0x13eaf0; prices are gold-tenths, paid from POOLED
/// party gold like the original's fcn.00067b9c):
///   Heal        — unk6 = gold per missing LP
///   Cure        — unk6 = flat gold, clears Poisoned/Ill
///   RemoveCurse — unk6 = flat gold, DESTROYS all cursed equipped items
///   AskOpinion  — identify: price = item value × unk6/100, sets the ExtraInfo flag
///   RestoreItemEnergy — unk6 = gold per charge (spell items), enchant counter +1/service
///   SleepInRoom — unk6 = flat gold, rests 8 hours
///   Merchant / ScrollMerchant — open the merchant screen for merchant id = unk8
///   OrderFood   — unk6 = gold per ration
///   LearnCloseCombat — unk6 = gold per +1 skill point (costs 1 training point too)
///   LearnSpells — school = unk5; price = unk6 × spell level requirement; learning sets
///                 the known bit and initial mastery = 4 × MagicTalent
/// PLACEHOLDER: Unk3/Unk4 confirm/success text overrides aren't shown.
/// </summary>
public class PlaceActionManager : GameComponent
{
    public PlaceActionManager()
    {
        On<PlaceActionEvent>(OnPlaceAction);
        On<ServiceHealEvent>(DoHeal);
        On<ServiceTrainEvent>(DoTrain);
        On<ServiceBuyFoodEvent>(DoBuyFood);
        On<ServiceSpellListEvent>(ShowSpellList);
        On<ServiceLearnSpellEvent>(DoLearnSpell);
        On<ServiceRepairEvent>(DoRepair);
        On<ServiceRechargeEvent>(DoRecharge);
        On<ServiceIdentifyEvent>(DoIdentify);
    }

    [Event("svc_heal")] public record ServiceHealEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("rate")] ushort GoldPerLp) : EventRecord;
    [Event("svc_train")] public record ServiceTrainEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("skill")] Skill Skill, [property: EventPart("gold")] ushort Gold) : EventRecord;
    [Event("svc_food")] public record ServiceBuyFoodEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("price")] ushort Price, [property: EventPart("amount")] ushort Amount) : EventRecord;
    [Event("svc_spells")] public record ServiceSpellListEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("school")] UAlbion.Formats.Assets.SpellClass School, [property: EventPart("rate")] ushort GoldPerLevel) : EventRecord;
    [Event("svc_learn")] public record ServiceLearnSpellEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("school")] UAlbion.Formats.Assets.SpellClass School, [property: EventPart("num")] ushort SpellNumber, [property: EventPart("price")] ushort Price) : EventRecord;
    [Event("svc_repair")] public record ServiceRepairEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("slot")] UAlbion.Formats.Assets.Inv.ItemSlotId SlotId, [property: EventPart("price")] ushort Price) : EventRecord;
    [Event("svc_recharge")] public record ServiceRechargeEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("slot")] UAlbion.Formats.Assets.Inv.ItemSlotId SlotId, [property: EventPart("charges")] ushort Charges, [property: EventPart("price")] ushort Price) : EventRecord;
    [Event("svc_identify")] public record ServiceIdentifyEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("slot")] UAlbion.Formats.Assets.Inv.ItemSlotId SlotId, [property: EventPart("price")] ushort Price) : EventRecord;

    void OnPlaceAction(PlaceActionEvent e)
    {
        switch (e.Type)
        {
            case PlaceActionType.Heal:              ShowHealMenu(e.Unk6); break;
            case PlaceActionType.Cure:              Cure(e.Unk6); break;
            case PlaceActionType.SleepInRoom:       Sleep(e.Unk6); break;
            case PlaceActionType.OrderFood:         ShowFoodMenu(e.Unk6); break;
            case PlaceActionType.LearnCloseCombat:  ShowTrainMenu(Skill.Melee, e.Unk6); break;
            case PlaceActionType.LearnSpells:       ShowLearnSpellsMenu((UAlbion.Formats.Assets.SpellClass)e.Unk5, e.Unk6); break;
            case PlaceActionType.RepairItem:        ShowRepairMenu(e.Unk6); break;
            case PlaceActionType.RestoreItemEnergy: ShowRechargeMenu(e.Unk6); break;
            case PlaceActionType.RemoveCurse:       RemoveCurse(e.Unk6); break;
            case PlaceActionType.AskOpinion:        ShowIdentifyMenu(e.Unk6); break;
            case PlaceActionType.Merchant:
            case PlaceActionType.ScrollMerchant:
                // ScrollMerchant shares the Merchant handler verbatim in the original
                // (identical function pointer); wares = the merchant inventory in Unk8.
                Raise(new MerchantEvent(new MerchantId(AssetType.Merchant, e.Unk8), PartyMemberId.None));
                break;
            default:
                Info($"[PlaceAction] {e.Type} not implemented yet (unk2={e.Unk2} unk5={e.Unk5} unk6={e.Unk6})");
                break;
        }
    }

    IPlayer Leader => Resolve<IParty>().Leader;

    int PartyGold()
    {
        int total = 0;
        foreach (var member in Resolve<IParty>().StatusBarOrder)
            total += member?.Effective?.Inventory?.Gold?.Amount ?? 0;
        return total;
    }

    bool TrySpendGold(int amount)
    {
        // Pooled party gold (the original's fcn.00067b9c): deducted member by member.
        if (amount < 0 || PartyGold() < amount)
        {
            Info($"[PlaceAction] Not enough gold ({PartyGold()} < {amount})");
            return false;
        }

        int remaining = amount;
        foreach (var member in Resolve<IParty>().StatusBarOrder)
        {
            if (remaining <= 0)
                break;
            int gold = member?.Effective?.Inventory?.Gold?.Amount ?? 0;
            if (gold <= 0)
                continue;
            int take = Math.Min(gold, remaining);
            Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, member.Id.Id), ChangeProperty.Gold, NumericOperation.SubtractAmount, (ushort)take));
            remaining -= take;
        }
        return true;
    }

    void ShowMenu(string heading, List<ContextMenuOption> options)
    {
        if (options.Count == 0)
            return;
        var window = Resolve<UAlbion.Core.IGameWindow>();
        var cursor = Resolve<Input.ICursorManager>();
        Raise(new ContextMenuEvent(window.PixelToUi(cursor.Position), new LiteralText(heading), options));
    }

    void ShowHealMenu(ushort goldPerLp)
    {
        var party = Resolve<IParty>();
        var language = ReadVar(V.User.Gameplay.Language);
        var options = new List<ContextMenuOption>();
        foreach (var member in party.StatusBarOrder)
        {
            var lp = member.Effective?.Combat?.LifePoints;
            if (lp == null || lp.Current >= lp.Max)
                continue;
            int missing = lp.Max - lp.Current;
            int cost = missing * goldPerLp;
            options.Add(new ContextMenuOption(
                new LiteralText($"{member.Effective.GetName(language)} ({missing} LP, {cost} gold)"),
                new ServiceHealEvent(member.Id, goldPerLp),
                ContextMenuGroup.Actions));
        }
        ShowMenu("Heal", options);
    }

    void DoHeal(ServiceHealEvent e)
    {
        var party = Resolve<IParty>();
        foreach (var member in party.StatusBarOrder)
        {
            if (member.Id != e.MemberId)
                continue;
            var lp = member.Effective?.Combat?.LifePoints;
            if (lp == null) return;
            int missing = lp.Max - lp.Current;
            if (missing <= 0 || !TrySpendGold(missing * e.GoldPerLp))
                return;
            Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, e.MemberId.Id), ChangeProperty.Health, NumericOperation.AddAmount, (ushort)missing));
            Info($"[PlaceAction] Healed {e.MemberId} for {missing} LP ({missing * e.GoldPerLp} gold)");
            return;
        }
    }

    void Cure(ushort gold)
    {
        if (!TrySpendGold(gold))
            return;
        var party = Resolve<IParty>();
        foreach (var member in party.StatusBarOrder)
        {
            var target = new TargetId(AssetType.PartyMember, member.Id.Id);
            Raise(new ChangeStatusEvent(target, PlayerCondition.Poisoned, NumericOperation.SubtractAmount, 1));
            Raise(new ChangeStatusEvent(target, PlayerCondition.Ill, NumericOperation.SubtractAmount, 1));
        }
        Info($"[PlaceAction] Cured the party ({gold} gold)");
    }

    void Sleep(ushort gold)
    {
        if (!TrySpendGold(gold))
            return;
        Info($"[PlaceAction] Sleeping in room ({gold} gold)");
        Raise(new RestEvent(8));
    }

    void ShowFoodMenu(ushort price)
    {
        var options = new List<ContextMenuOption>();
        foreach (ushort amount in new ushort[] { 1, 5, 10 })
        {
            options.Add(new ContextMenuOption(
                new LiteralText($"{amount} rations ({amount * price} gold)"),
                new ServiceBuyFoodEvent(Leader.Id, price, amount),
                ContextMenuGroup.Actions));
        }
        ShowMenu("Order food", options);
    }

    void DoBuyFood(ServiceBuyFoodEvent e)
    {
        if (!TrySpendGold(e.Price * e.Amount))
            return;
        Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, e.MemberId.Id), ChangeProperty.Food, NumericOperation.AddAmount, e.Amount));
        Info($"[PlaceAction] Bought {e.Amount} rations ({e.Price * e.Amount} gold)");
    }

    void ShowTrainMenu(Skill skill, ushort goldPerPoint)
    {
        var party = Resolve<IParty>();
        var language = ReadVar(V.User.Gameplay.Language);
        var options = new List<ContextMenuOption>();
        foreach (var member in party.StatusBarOrder)
        {
            int tp = member.Effective?.Combat?.TrainingPoints ?? 0;
            if (tp < 1)
                continue;
            options.Add(new ContextMenuOption(
                new LiteralText($"{member.Effective.GetName(language)} (+1, 1 TP + {goldPerPoint} gold)"),
                new ServiceTrainEvent(member.Id, skill, goldPerPoint),
                ContextMenuGroup.Actions));
        }
        ShowMenu("Train", options);
    }

    void DoTrain(ServiceTrainEvent e)
    {
        var party = Resolve<IParty>();
        foreach (var member in party.StatusBarOrder)
        {
            if (member.Id != e.MemberId)
                continue;
            int tp = member.Effective?.Combat?.TrainingPoints ?? 0;
            if (tp < 1 || !TrySpendGold(e.Gold))
                return;
            var target = new TargetId(AssetType.PartyMember, e.MemberId.Id);
            Raise(new DataChangeEvent(target, ChangeProperty.TrainingPoints, NumericOperation.SubtractAmount, 1));
            Raise(new ChangeSkillEvent(target, e.Skill, NumericOperation.AddAmount, 1));
            Info($"[PlaceAction] Trained {e.MemberId} {e.Skill} +1 (1 TP + {e.Gold} gold)");
            return;
        }
    }

    IPlayer FindMember(PartyMemberId id)
    {
        foreach (var member in Resolve<IParty>().StatusBarOrder)
            if (member.Id == id)
                return member;
        return null;
    }

    // --- LearnSpells (type 0xB, RE batch 4): school = Unk5, the 30 school spells gated
    // by SpellData.LevelRequirement vs the member's level; price = Unk6 × LevelRequirement;
    // learning sets the known bit + initial mastery 4 × MagicTalent (no SLP cost). ---

    void ShowLearnSpellsMenu(UAlbion.Formats.Assets.SpellClass school, ushort goldPerLevel)
    {
        var party = Resolve<IParty>();
        var language = ReadVar(V.User.Gameplay.Language);
        var schoolFlag = (SpellClasses)(1 << (int)school);
        var options = new List<ContextMenuOption>();
        foreach (var member in party.StatusBarOrder)
        {
            if (((member.Effective?.Magic?.SpellClasses ?? SpellClasses.None) & schoolFlag) == 0)
                continue;
            options.Add(new ContextMenuOption(
                new LiteralText(member.Effective.GetName(language)),
                new ServiceSpellListEvent(member.Id, school, goldPerLevel),
                ContextMenuGroup.Actions));
        }
        ShowMenu("Learn spells", options);
    }

    void ShowSpellList(ServiceSpellListEvent e)
    {
        var member = FindMember(e.MemberId);
        if (member == null)
            return;
        var spellManager = Resolve<UAlbion.Formats.ISpellManager>();
        int level = member.Effective?.Level ?? 1;
        var known = member.Effective?.Magic?.KnownSpells;
        var options = new List<ContextMenuOption>();
        for (byte n = 0; n < 30; n++)
        {
            var spellId = spellManager.GetSpellId(e.School, n);
            if (spellId.IsNone)
                continue;
            var spell = spellManager.GetSpellOrDefault(spellId);
            if (spell == null || spell.LevelRequirement > level)
                continue;
            if (spell.LevelRequirement == 0)
                continue; // unused slots in the school's 30-entry table (no real spell)
            if (known != null && known.Contains(spellId))
                continue;
            int price = e.GoldPerLevel * spell.LevelRequirement;
            options.Add(new ContextMenuOption(
                new LiteralText($"{spellId.ToString().Split('.')[^1]} ({(decimal)price / 10:0.#} gold)"),
                new ServiceLearnSpellEvent(e.MemberId, e.School, n, (ushort)price),
                ContextMenuGroup.Actions));
        }
        ShowMenu("Learn which spell?", options);
    }

    void DoLearnSpell(ServiceLearnSpellEvent e)
    {
        if (FindMember(e.MemberId) == null || !TrySpendGold(e.Price))
            return;
        // SheetApplier sets the known bit and seeds mastery = 4 × MagicTalent.
        Raise(new ChangeSpellsEvent(new TargetId(AssetType.PartyMember, e.MemberId.Id), e.School, e.SpellNumber, NumericOperation.SetToMaximum));
        Info($"[PlaceAction] {e.MemberId} learned {e.School} spell {e.SpellNumber} ({e.Price} gold-tenths)");
    }

    // --- RepairItem (type 0xC): price = item value × Unk6/100; clears the Broken flag. ---

    void ShowRepairMenu(ushort pricePct)
    {
        var options = new List<ContextMenuOption>();
        ForEachItemSlot((member, slotId, slot, item) =>
        {
            if ((slot.Flags & UAlbion.Formats.Assets.Inv.ItemSlotFlags.Broken) == 0)
                return;
            int price = Math.Max(1, item.Value * pricePct / 100);
            options.Add(new ContextMenuOption(
                new LiteralText($"{ItemName(item)} ({(decimal)price / 10:0.#} gold)"),
                new ServiceRepairEvent(member.Id, slotId, (ushort)price),
                ContextMenuGroup.Actions));
        });
        ShowMenu("Repair what?", options);
    }

    void DoRepair(ServiceRepairEvent e)
    {
        if (!TrySpendGold(e.Price))
            return;
        Raise(new RepairInventorySlotEvent(e.MemberId, e.SlotId));
    }

    // --- RestoreItemEnergy (type 0x5): spell items only; price = Unk6 per charge,
    // count = min(missing, affordable); enchant counter +1 per service. ---

    void ShowRechargeMenu(ushort pricePerCharge)
    {
        var options = new List<ContextMenuOption>();
        ForEachItemSlot((member, slotId, slot, item) =>
        {
            if (item.Spell.IsNone || item.MaxCharges == 0 || slot.Charges >= item.MaxCharges)
                return;
            int missing = item.MaxCharges - slot.Charges;
            int affordable = pricePerCharge > 0 ? PartyGold() / pricePerCharge : missing;
            int count = Math.Min(missing, affordable);
            if (count <= 0)
                return;
            int price = count * pricePerCharge;
            options.Add(new ContextMenuOption(
                new LiteralText($"{ItemName(item)} (+{count} charges, {(decimal)price / 10:0.#} gold)"),
                new ServiceRechargeEvent(member.Id, slotId, (ushort)count, (ushort)price),
                ContextMenuGroup.Actions));
        });
        ShowMenu("Recharge what?", options);
    }

    void DoRecharge(ServiceRechargeEvent e)
    {
        if (!TrySpendGold(e.Price))
            return;
        Raise(new RechargeInventorySlotEvent(e.MemberId, e.SlotId, e.Charges));
    }

    // --- RemoveCurse (type 0x3): flat price; DESTROYS all cursed equipped items. ---

    void RemoveCurse(ushort gold)
    {
        if (!TrySpendGold(gold))
            return;
        foreach (var member in Resolve<IParty>().StatusBarOrder)
            Raise(new DestroyCursedEquipmentEvent(member.Id));
        Info($"[PlaceAction] Removed curses ({gold} gold-tenths) — cursed equipment destroyed");
    }

    // --- AskOpinion (type 0x4): identify — price = item value × Unk6/100, sets ExtraInfo. ---

    void ShowIdentifyMenu(ushort pricePct)
    {
        var options = new List<ContextMenuOption>();
        ForEachItemSlot((member, slotId, slot, item) =>
        {
            if ((slot.Flags & UAlbion.Formats.Assets.Inv.ItemSlotFlags.ExtraInfo) != 0)
                return;
            int price = Math.Max(1, item.Value * pricePct / 100);
            options.Add(new ContextMenuOption(
                new LiteralText($"{ItemName(item)} ({(decimal)price / 10:0.#} gold)"),
                new ServiceIdentifyEvent(member.Id, slotId, (ushort)price),
                ContextMenuGroup.Actions));
        });
        ShowMenu("Identify what?", options);
    }

    void DoIdentify(ServiceIdentifyEvent e)
    {
        if (!TrySpendGold(e.Price))
            return;
        Raise(new IdentifyInventorySlotEvent(e.MemberId, e.SlotId));
    }

    // --- helpers ---

    string ItemName(UAlbion.Formats.Assets.Inv.ItemData item)
        => Assets.LoadStringSafe(item.Name);

    void ForEachItemSlot(Action<IPlayer, UAlbion.Formats.Assets.Inv.ItemSlotId, UAlbion.Formats.Assets.Inv.IReadOnlyItemSlot, UAlbion.Formats.Assets.Inv.ItemData> visit)
    {
        foreach (var member in Resolve<IParty>().StatusBarOrder)
        {
            var inv = member?.Effective?.Inventory;
            if (inv == null)
                continue;
            for (var slotId = (UAlbion.Formats.Assets.Inv.ItemSlotId)0; slotId < UAlbion.Formats.Assets.Inv.ItemSlotId.FullSlotCount; slotId++)
            {
                var slot = inv.GetSlot(slotId);
                if (slot == null || slot.Item.Type != AssetType.Item)
                    continue;
                var item = Assets.LoadItem(slot.Item);
                if (item == null)
                    continue;
                visit(member, slotId, slot, item);
            }
        }
    }
}
