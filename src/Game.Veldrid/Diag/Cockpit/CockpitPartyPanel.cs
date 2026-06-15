using ImGuiNET;
using UAlbion.Game.Settings;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>Tab B — Party &amp; Stats. Live read of each member plus recruit/dismiss/leader/language
/// writes (via existing events). HP/SP editing is deferred (no simple set-health event yet).</summary>
public sealed class CockpitPartyPanel
{
    readonly PlaythroughCockpitWindow _owner;
    readonly byte[] _addBuffer = new byte[64];

    public CockpitPartyPanel(PlaythroughCockpitWindow owner) => _owner = owner;

    public void Draw()
    {
        var party = _owner.Party;
        if (party == null || _owner.State is not { Loaded: true })
        {
            ImGui.TextDisabled("No game loaded.");
            return;
        }

        ImGui.Text($"Party gold: {party.TotalGold}   rations: {party.TotalRations}");

        var dbg = UserVars.Instance.Debug;
        bool god = _owner.ReadFlag(dbg.GodMode);
        if (ImGui.Checkbox("God mode (party takes no combat damage)", ref god))
            _owner.WriteFlag(dbg.GodMode, god);
        bool osk = _owner.ReadFlag(dbg.OneShotKill);
        if (ImGui.Checkbox("One-shot kill (monsters die in one hit)", ref osk))
            _owner.WriteFlag(dbg.OneShotKill, osk);

        ImGui.Separator();

        foreach (var pm in party.StatusBarOrder)
        {
            var combat = pm?.Apparent?.Combat;
            if (combat == null)
                continue;

            var sp = pm.Apparent.Magic?.SpellPoints;
            string id = pm.Id.ToString();
            bool isLeader = party.Leader?.Id == pm.Id;

            ImGui.Text(
                $"{id}{(isLeader ? " *" : "")}  L{pm.Apparent.Level}  " +
                $"HP {combat.LifePoints?.Current ?? 0}/{combat.LifePoints?.Max ?? 0}  " +
                $"SP {sp?.Current ?? 0}/{sp?.Max ?? 0}");

            string cond = combat.Conditions.ToString();
            if (!string.IsNullOrEmpty(cond) && cond != "0" && cond != "None")
                ImGui.TextDisabled($"    conditions: {cond}");

            if (!isLeader)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Leader##{id}")) _owner.RunCommand($"set_party_leader {id}");
            }
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##{id}")) _owner.RunCommand($"remove_party_member {id}");
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(160);
        ImGui.InputText("##addmember", _addBuffer, (uint)_addBuffer.Length);
        ImGui.SameLine();
        if (ImGui.Button("Add member"))
        {
            var who = PlaythroughCockpitWindow.ReadBuffer(_addBuffer);
            if (!string.IsNullOrWhiteSpace(who))
                _owner.RunCommand($"add_party_member {who}");
        }
        ImGui.TextDisabled("Add: type a PartyMember id in the form shown above (e.g. the '*'/Remove labels).");

        ImGui.Separator();
        ImGui.Text("Language:");
        ImGui.SameLine(); if (ImGui.SmallButton("English")) _owner.RunCommand("set_language English");
        ImGui.SameLine(); if (ImGui.SmallButton("German"))  _owner.RunCommand("set_language German");
        ImGui.SameLine(); if (ImGui.SmallButton("French"))  _owner.RunCommand("set_language French");
    }
}
