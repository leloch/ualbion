using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using ImGuiNET;
using UAlbion.Api.Eventing;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Veldrid.Diag;

namespace UAlbion.Game.Veldrid.Diag;

public class ImGuiConsoleLogger : Component, IImGuiWindow
{
    // TODO: Initial size
    readonly byte[] _inputBuffer = new byte[512];
    bool _autoScroll = true;
    bool _scrollToBottom = true;
    bool _focus;

    // #44/#45: command history (Up/Down) and Tab autocomplete of event command names.
    readonly List<string> _history = [];
    int _historyPos = -1; // -1 = editing a fresh line; otherwise an index into _history
    string[] _commandNames;
    readonly ImGuiInputTextCallback _callback;

    public string Name { get; }
    public ImGuiConsoleLogger(string name)
    {
        Name = name;
        On<FocusConsoleEvent>(_ => _focus = true);
        unsafe { _callback = TextEditCallback; } // cached so the delegate isn't re-marshalled every frame
    }

    string[] CommandNames =>
        _commandNames ??= EventSerializer.Instance
            .GetEventMetadata()
            .Select(m => m.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ImGuiWindowDrawResult Draw()
    {
        var window = Resolve<IGameWindow>();
        bool open = true;
        ImGui.Begin(Name, ref open);
        ImGui.SetWindowPos(Vector2.Zero, ImGuiCond.FirstUseEver);
        ImGui.SetWindowSize(new Vector2(window.PixelWidth / 3.0f, window.PixelHeight), ImGuiCond.FirstUseEver);

        // Reserve enough left-over height for 1 separator + 1 input text
        float footerHeightToReserve = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild(
            "ScrollingRegion",
            new Vector2(0, -footerHeightToReserve),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);

        // Display every line as a separate entry so we can change their color or add custom widgets.
        // If you only want raw text you can use ImGui.TextUnformatted(log.begin(), log.end());
        // NB- if you have thousands of entries this approach may be too inefficient and may require user-side clipping
        // to only process visible items. The clipper will automatically measure the height of your first item and then
        // "seek" to display only items in the visible area.
        // To use the clipper we can replace your standard loop:
        //      for (int i = 0; i < Items.Size; i++)
        //   With:
        //      ImGuiListClipper clipper(Items.Size);
        //      while (clipper.Step())
        //         for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
        // - That your items are evenly spaced (same height)
        // - That you have cheap random access to your elements (you can access them given their index,
        //   without processing all the ones before)
        // You cannot this code as-is if a filter is active because it breaks the 'cheap random-access' property.
        // We would need random-access on the post-filtered list.
        // A typical application wanting coarse clipping and filtering may want to pre-compute an array of indices
        // or offsets of items that passed the filtering test, recomputing this array when user changes the filter,
        // and appending newly elements as they are inserted. This is left as a task to the user until we can manage
        // to improve this example code!
        // If your items are of variable height:
        // - Split them into same height items would be simpler and facilitate random-seeking into your list.
        // - Consider using manual call to IsRectVisible() and skipping extraneous decoration from your items.

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4,1)); // Tighten spacing

        var history = Resolve<ILogHistory>();
        history.Access(0, (_, logs) =>
        {
            foreach (var log in logs)
            {
                //if (!Filter.PassFilter(item))
                //    continue;

                // Normally you would store more information in your item than just a string.
                // (e.g. make Items[] an array of structure, store color/type etc.)
                ImGui.PushStyleColor(ImGuiCol.Text, ConsoleColorToRgba(log.Color));
                ImGui.Indent(log.Nesting);
                ImGui.TextUnformatted(log.Message);
                ImGui.Unindent(log.Nesting);
                ImGui.PopStyleColor();
            }
        });

        if (_scrollToBottom || (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY()))
            ImGui.SetScrollHereY(1.0f);
        _scrollToBottom = false;

        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.Separator();

        // Command-line
        bool reclaimFocus = false;
        ImGuiInputTextFlags inputTextFlags =
            ImGuiInputTextFlags.EnterReturnsTrue
          | ImGuiInputTextFlags.CallbackCompletion // #45: Tab to autocomplete command names
          | ImGuiInputTextFlags.CallbackHistory;    // #44: Up/Down to browse submitted commands

        if (_focus)
        {
            ImGui.SetKeyboardFocusHere(0);
            _focus = false;
        }

        if (ImGui.InputText("##input", _inputBuffer, (uint)_inputBuffer.Length, inputTextFlags, _callback))
        {
            var logExchange = Resolve<ILogExchange>();
            var command = Encoding.ASCII.GetString(_inputBuffer);
            command = command[..command.IndexOf((char)0, StringComparison.Ordinal)];
            for (int i = 0; i < command.Length; i++)
                _inputBuffer[i] = 0;

            // #44: record non-blank commands in history (most-recent last, de-duplicating a repeat
            // of the immediately-previous entry) and reset the browse cursor.
            var trimmed = command.Trim();
            if (trimmed.Length > 0 && (_history.Count == 0 || _history[^1] != trimmed))
                _history.Add(trimmed);
            _historyPos = -1;

            IEvent parsedEvent = Event.Parse(command, out var error);
            if (parsedEvent != null)
                logExchange.EnqueueEvent(parsedEvent);
            else
                PrintMessage(logExchange, error, LogLevel.Error);

            reclaimFocus = true;
        }

        ImGui.SetItemDefaultFocus();
        if (reclaimFocus)
            ImGui.SetKeyboardFocusHere(-1); // Auto focus previous widget

        ImGui.SameLine();
        ImGui.Checkbox("Scroll", ref _autoScroll);

        ImGui.End();

        return open ? ImGuiWindowDrawResult.None : ImGuiWindowDrawResult.Closed;
    }

    void PrintMessage(ILogExchange logExchange, string message, LogLevel level)
        => logExchange.Receive(new LogEvent(level, message), this);

    // #44/#45: InputText callback for Tab-completion of command names and Up/Down history browsing.
    // Ported from the Dear ImGui demo console to ImGui.NET (the original C++ stub was left commented).
    unsafe int TextEditCallback(ImGuiInputTextCallbackData* dataPtr)
    {
        ImGuiInputTextCallbackDataPtr data = dataPtr;
        switch (data.EventFlag)
        {
            case ImGuiInputTextFlags.CallbackCompletion:
            {
                var buf = (byte*)data.Buf;
                int wordEnd = data.CursorPos;
                int wordStart = wordEnd;
                while (wordStart > 0)
                {
                    byte c = buf[wordStart - 1];
                    if (c == (byte)' ' || c == (byte)'\t' || c == (byte)',' || c == (byte)';')
                        break;
                    wordStart--;
                }

                string prefix = wordEnd > wordStart
                    ? Encoding.ASCII.GetString(buf + wordStart, wordEnd - wordStart)
                    : string.Empty;

                var candidates = CommandNames
                    .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var logExchange = TryResolve<ILogExchange>();
                if (candidates.Count == 0)
                {
                    if (logExchange != null && prefix.Length > 0)
                        PrintMessage(logExchange, $"No command matches \"{prefix}\"", LogLevel.Warning);
                }
                else if (candidates.Count == 1)
                {
                    data.DeleteChars(wordStart, wordEnd - wordStart);
                    data.InsertChars(data.CursorPos, candidates[0] + " ");
                }
                else
                {
                    // Complete the longest common (case-insensitive) prefix shared by all candidates.
                    int matchLen = prefix.Length;
                    bool grow = true;
                    while (grow)
                    {
                        char? c = null;
                        for (int i = 0; i < candidates.Count && grow; i++)
                        {
                            if (matchLen >= candidates[i].Length) { grow = false; break; }
                            char cc = char.ToUpperInvariant(candidates[i][matchLen]);
                            if (c == null) c = cc;
                            else if (c != cc) grow = false;
                        }
                        if (grow) matchLen++;
                    }

                    if (matchLen > prefix.Length)
                    {
                        data.DeleteChars(wordStart, wordEnd - wordStart);
                        data.InsertChars(data.CursorPos, candidates[0].Substring(0, matchLen));
                    }

                    if (logExchange != null)
                    {
                        PrintMessage(logExchange, "Matches:", LogLevel.Info);
                        foreach (var c in candidates)
                            PrintMessage(logExchange, "  " + c, LogLevel.Info);
                    }
                }
                break;
            }

            case ImGuiInputTextFlags.CallbackHistory:
            {
                int prevPos = _historyPos;
                if (data.EventKey == ImGuiKey.UpArrow)
                {
                    if (_historyPos == -1) _historyPos = _history.Count - 1;
                    else if (_historyPos > 0) _historyPos--;
                }
                else if (data.EventKey == ImGuiKey.DownArrow)
                {
                    if (_historyPos != -1 && ++_historyPos >= _history.Count)
                        _historyPos = -1;
                }

                if (prevPos != _historyPos)
                {
                    string historyStr = _historyPos >= 0 && _historyPos < _history.Count ? _history[_historyPos] : string.Empty;
                    data.DeleteChars(0, data.BufTextLen);
                    if (historyStr.Length > 0)
                        data.InsertChars(0, historyStr);
                }
                break;
            }
        }

        return 0;
    }

    static Vector4 ConsoleColorToRgba(ConsoleColor color) => color switch
    {
        ConsoleColor.White => new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
        ConsoleColor.Cyan => new Vector4(0.3f, 1.0f, 1.0f, 1.0f),
        ConsoleColor.Red => new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
        ConsoleColor.Yellow => new Vector4(1.0f, 1.0f, 0.3f, 1.0f),
        _ => new Vector4(0.85f, 0.85f, 0.85f, 1.0f),
    };
}
