using System;
using System.Collections.Generic;
using System.Text;
using ImGuiNET;
using UAlbion.Api.Eventing;
using UAlbion.Api.Settings;
using UAlbion.Core.Veldrid.Diag;
using UAlbion.Game.State;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>
/// The Playthrough Test Cockpit — a single tabbed ImGui dev window for setting up and inspecting
/// arbitrary game state while playing, so the back half of the game can be tested without
/// save-scumming. Most controls are thin front-ends over the same event vocabulary the HTTP
/// harness drives (<see cref="RunCommand"/>), so anything clickable here is also scriptable.
///
/// Open it from the F1 debug overlay (Windows/Debug/Cockpit), the `show_cockpit` event, or the
/// Ctrl+Shift+C hotkey.
/// </summary>
public sealed class PlaythroughCockpitWindow : Component, IImGuiWindow
{
    public readonly record struct FirehoseEntry(string Message, bool Flagged);

    const int MaxFirehose = 500;

    readonly CockpitScenarioPanel _scenario;
    readonly CockpitPartyPanel _party;
    readonly CockpitInventoryPanel _inventory;
    readonly CockpitQuestPanel _quest;
    readonly CockpitMapPanel _map;
    readonly CockpitTimePanel _time;
    readonly CockpitFirehosePanel _firehose;

    readonly object _firehoseLock = new();
    readonly List<FirehoseEntry> _firehoseBuffer = new();
    EventHandler<LogEventArgs> _logHandler;
    ILogExchange _logExchange;

    public string Name { get; }

    public PlaythroughCockpitWindow(string name)
    {
        Name = name;
        _scenario = new CockpitScenarioPanel(this);
        _party = new CockpitPartyPanel(this);
        _inventory = new CockpitInventoryPanel(this);
        _quest = new CockpitQuestPanel(this);
        _map = new CockpitMapPanel(this);
        _time = new CockpitTimePanel(this);
        _firehose = new CockpitFirehosePanel(this);
    }

    public ImGuiWindowDrawResult Draw()
    {
        EnsureFirehoseSubscribed();

        bool open = true;
        ImGui.Begin(Name, ref open);

        if (ImGui.BeginTabBar("cockpit_tabs"))
        {
            if (ImGui.BeginTabItem("Scenario"))  { _scenario.Draw();  ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Party"))     { _party.Draw();     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Inventory")) { _inventory.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Quests"))    { _quest.Draw();     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Map"))       { _map.Draw();       ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Firehose"))  { _firehose.Draw();  ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Time"))      { _time.Draw();      ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Finale"))    { DrawFinaleStub();  ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        ImGui.End();

        if (!open)
        {
            UnsubscribeFirehose();
            return ImGuiWindowDrawResult.Closed;
        }
        return ImGuiWindowDrawResult.None;
    }

    static void DrawFinaleStub()
    {
        ImGui.TextDisabled("Finale rig");
        ImGui.Text("Pending B2/B3 (ask_surrender handler + Seed/Endgame sequencer).");
        ImGui.TextDisabled("Will force the combat-surrender outcome and step the Endgame clips once wired.");
    }

    // --- Firehose (Tab F) --------------------------------------------------------------------

    void EnsureFirehoseSubscribed()
    {
        if (_logHandler != null)
            return;
        _logExchange = TryResolve<ILogExchange>();
        if (_logExchange == null)
            return;
        _logHandler = OnLog;
        _logExchange.Log += _logHandler;
    }

    void UnsubscribeFirehose()
    {
        if (_logHandler == null || _logExchange == null)
            return;
        _logExchange.Log -= _logHandler;
        _logHandler = null;
    }

    void OnLog(object sender, LogEventArgs e)
    {
        if (string.IsNullOrEmpty(e?.Message))
            return;

        bool flagged =
            e.Color == ConsoleColor.Red
            || e.Message.Contains("Unk", StringComparison.Ordinal)
            || e.Message.Contains("QueryType", StringComparison.Ordinal)
            || e.Message.Contains("Exception", StringComparison.Ordinal);

        var entry = new FirehoseEntry($"{e.Time:HH:mm:ss} {e.Message}", flagged);
        lock (_firehoseLock)
        {
            _firehoseBuffer.Add(entry);
            if (_firehoseBuffer.Count > MaxFirehose)
                _firehoseBuffer.RemoveRange(0, _firehoseBuffer.Count - MaxFirehose);
        }
    }

    internal FirehoseEntry[] FirehoseSnapshot()
    {
        lock (_firehoseLock)
            return _firehoseBuffer.ToArray();
    }

    internal void ClearFirehose()
    {
        lock (_firehoseLock)
            _firehoseBuffer.Clear();
    }

    // --- Helpers shared with the panels (which are plain classes, not Components) -------------

    internal IGameState State => TryResolve<IGameState>();
    internal IParty Party => TryResolve<IParty>();
    internal T Service<T>() => TryResolve<T>();
    internal void LogInfo(string message) => Info(message);

    internal bool ReadFlag(BoolVar v) => ReadVar(v);
    internal void WriteFlag(BoolVar v, bool value)
    {
        var settings = TryResolve<ISettings>();
        if (settings != null)
            v.Write(settings, value); // in-memory only (no SaveSettingsEvent) → resets on restart
    }

    /// <summary>Read a null-terminated ImGui InputText byte buffer as a string.</summary>
    internal static string ReadBuffer(byte[] buffer)
    {
        if (buffer == null)
            return null;
        int end = Array.IndexOf(buffer, (byte)0);
        if (end < 0) end = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, end);
    }

    /// <summary>
    /// Parse a harness/console-form command and enqueue it on the game thread. Routes through
    /// <see cref="ILogExchange"/> like the in-game console so behaviour is identical; parse
    /// errors are logged, never thrown (a throw in an ImGui Draw would tear the overlay).
    /// </summary>
    internal void RunCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        var evt = Event.Parse(command, out var error);
        if (evt == null)
        {
            Error($"[cockpit] parse error for '{command}': {error}");
            return;
        }

        var logExchange = TryResolve<ILogExchange>();
        if (logExchange != null)
            logExchange.EnqueueEvent(evt);
        else
            Enqueue(evt);

        Info($"[cockpit] {command}");
    }
}
