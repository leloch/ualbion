using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Config;
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
/// silent no-ops. Pricing semantics taken from the on-disk values (e.g. Rejira: Heal
/// unk6=2 gold/LP, Cure 100, RemoveCurse 300; Zirr: room 12, food 5/ration; Ferina:
/// training 40 gold/point):
///   Heal        — unk6 = gold per missing LP
///   Cure        — unk6 = flat gold, clears Poisoned/Ill
///   SleepInRoom — unk6 = flat gold, rests 8 hours
///   OrderFood   — unk6 = gold per ration
///   LearnCloseCombat — unk6 = gold per +1 skill point (costs 1 training point too)
/// PLACEHOLDERs: gold is deducted from the LEADER (the original pools party gold);
/// unk2/3/4 limit semantics aren't decoded; LearnSpells / RepairItem /
/// RestoreItemEnergy / RemoveCurse / ScrollMerchant / AskOpinion log their request and
/// name what's missing.
/// </summary>
public class PlaceActionManager : GameComponent
{
    public PlaceActionManager()
    {
        On<PlaceActionEvent>(OnPlaceAction);
        On<ServiceHealEvent>(DoHeal);
        On<ServiceTrainEvent>(DoTrain);
        On<ServiceBuyFoodEvent>(DoBuyFood);
    }

    [Event("svc_heal")] public record ServiceHealEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("rate")] ushort GoldPerLp) : EventRecord;
    [Event("svc_train")] public record ServiceTrainEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("skill")] Skill Skill, [property: EventPart("gold")] ushort Gold) : EventRecord;
    [Event("svc_food")] public record ServiceBuyFoodEvent([property: EventPart("member")] PartyMemberId MemberId, [property: EventPart("price")] ushort Price, [property: EventPart("amount")] ushort Amount) : EventRecord;

    void OnPlaceAction(PlaceActionEvent e)
    {
        switch (e.Type)
        {
            case PlaceActionType.Heal:             ShowHealMenu(e.Unk6); break;
            case PlaceActionType.Cure:             Cure(e.Unk6); break;
            case PlaceActionType.SleepInRoom:      Sleep(e.Unk6); break;
            case PlaceActionType.OrderFood:        ShowFoodMenu(e.Unk6); break;
            case PlaceActionType.LearnCloseCombat: ShowTrainMenu(Skill.Melee, e.Unk6); break;
            default:
                Info($"[PlaceAction] {e.Type} not implemented yet (unk2={e.Unk2} unk5={e.Unk5} unk6={e.Unk6})");
                break;
        }
    }

    IPlayer Leader => Resolve<IParty>().Leader;
    int LeaderGold => Leader?.Effective?.Inventory?.Gold?.Amount ?? 0;

    bool TrySpendGold(int amount)
    {
        // PLACEHOLDER: deducts from the leader only; the original pools all members' gold.
        if (amount < 0 || LeaderGold < amount)
        {
            Info($"[PlaceAction] Not enough gold ({LeaderGold} < {amount})");
            return false;
        }

        if (amount > 0)
            Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, Leader.Id.Id), ChangeProperty.Gold, NumericOperation.SubtractAmount, (ushort)amount));
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
}
