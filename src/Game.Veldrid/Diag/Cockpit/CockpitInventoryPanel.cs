using ImGuiNET;
using UAlbion.Config;
using UAlbion.Formats.Assets.Inv;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>Tab C — Inventory &amp; Economy. Read per-member gold/rations/items; set or add party
/// gold (the B5 economy test instrument — read gold before/after a buy/sell).</summary>
public sealed class CockpitInventoryPanel
{
    readonly PlaythroughCockpitWindow _owner;
    int _gold = 1000;

    public CockpitInventoryPanel(PlaythroughCockpitWindow owner) => _owner = owner;

    public void Draw()
    {
        var party = _owner.Party;
        var state = _owner.State;
        if (party == null || state is not { Loaded: true })
        {
            ImGui.TextDisabled("No game loaded.");
            return;
        }

        ImGui.Text($"Party gold: {party.TotalGold}   rations: {party.TotalRations}");
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("##gold", ref _gold);
        if (_gold < 0) _gold = 0;
        ImGui.SameLine(); if (ImGui.SmallButton("Set gold")) _owner.RunCommand($"modify_gold SetAmount {_gold}");
        ImGui.SameLine(); if (ImGui.SmallButton("+ gold"))   _owner.RunCommand($"modify_gold AddAmount {_gold}");
        ImGui.Separator();

        foreach (var pm in party.StatusBarOrder)
        {
            if (pm == null)
                continue;
            var inv = state.GetInventory(new InventoryId(pm.Id));
            if (inv == null)
                continue;

            if (ImGui.TreeNode($"{pm.Id} — gold {inv.Gold?.Amount ?? 0}, rations {inv.Rations?.Amount ?? 0}##inv{pm.Id}"))
            {
                bool any = false;
                foreach (var slot in inv.EnumerateAll())
                {
                    if (slot == null || slot.Item.IsNone || slot.Item.Type != AssetType.Item)
                        continue;
                    any = true;
                    ImGui.BulletText($"{slot.Item} x{slot.Amount}");
                }
                if (!any)
                    ImGui.TextDisabled("(empty)");
                ImGui.TreePop();
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("Item-give: use the Console (`change_item ...`) — item targeting is per-item.");
    }
}
