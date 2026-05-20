using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UAlbion.Api.Eventing;
using UAlbion.Core.Events;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Diag;

/// <summary>
/// External-driver channel for the autonomous testing harness. Watches an "in.jsonl"-style
/// file inside the harness directory (one event-script command per line); each line is
/// parsed via the same <see cref="Event.Parse"/> path used by the <c>-c</c> CLI flag and
/// raised on the global event exchange.
/// </summary>
/// <remarks>
/// Why a file-watcher rather than a socket: zero-dependency, survives editor restarts,
/// and the harness driver (CI scripts, AI agents) can be anything that writes text.
///
/// The channel is enabled by <c>--harness &lt;dir&gt;</c> on the command line. It creates
/// the directory if it doesn't exist and writes a one-line "ready" record to
/// <c>out.jsonl</c> on first activation so external drivers know the game has started
/// processing commands.
///
/// Each polling pass:
/// 1. Reads any new bytes appended to <c>in.jsonl</c> since the last tick.
/// 2. Splits into lines; ignores blanks and comments (lines starting with <c>#</c>).
/// 3. For each line, calls <see cref="Event.Parse"/> and <see cref="Component.Raise"/>.
/// 4. Writes a per-line outcome record to <c>out.jsonl</c> (status: ok / parse-error /
///    raise-exception) so the driver can confirm execution and grab error text.
///
/// State observation flows through the sibling <see cref="DumpStateEvent"/> — the driver
/// raises <c>dump_state &lt;path&gt;</c> and reads the produced JSON.
/// </remarks>
public sealed class HarnessChannel : Component
{
    readonly string _inPath;
    readonly string _outPath;
    long _readOffset;
    int _commandsProcessed;
    StringBuilder _carry = new();

    public HarnessChannel(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Directory.CreateDirectory(directory);
        _inPath = Path.Combine(directory, "in.jsonl");
        _outPath = Path.Combine(directory, "out.jsonl");

        // Touch input file so the driver knows the path exists.
        if (!File.Exists(_inPath))
            File.WriteAllText(_inPath, "");

        // EngineUpdateEvent fires every frame from Engine.InnerLoop() — including on the
        // main menu, before any save loads. FastClockEvent only fires once GameClock is
        // running (post-load), which made the channel silent on the menu.
        On<EngineUpdateEvent>(_ => Poll());
        On<DumpStateEvent>(OnDumpState);
    }

    protected override void Subscribed()
        => WriteOut($"{{\"event\":\"harness_ready\",\"in\":{Json(_inPath)},\"out\":{Json(_outPath)}}}");

    void Poll()
    {
        if (!File.Exists(_inPath)) return;
        try
        {
            using var fs = new FileStream(_inPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= _readOffset) return;
            fs.Position = _readOffset;
            using var reader = new StreamReader(fs);
            string chunk = reader.ReadToEnd();
            _readOffset = fs.Length;
            ProcessChunk(chunk);
        }
        catch (IOException ex)
        {
            WriteOut($"{{\"event\":\"harness_io_error\",\"error\":{Json(ex.Message)}}}");
        }
    }

    void ProcessChunk(string chunk)
    {
        _carry.Append(chunk);
        var text = _carry.ToString();
        int lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0) return;

        string completed = text[..lastNewline];
        _carry.Clear();
        _carry.Append(text[(lastNewline + 1)..]);

        foreach (var rawLine in completed.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            RunCommand(line);
        }
    }

    void RunCommand(string command)
    {
        _commandsProcessed++;
        var evt = Event.Parse(command, out var error);
        if (evt == null)
        {
            WriteOut($"{{\"event\":\"harness_parse_error\",\"seq\":{_commandsProcessed},\"command\":{Json(command)},\"error\":{Json(error)}}}");
            return;
        }

        try
        {
            Raise(evt);
            WriteOut($"{{\"event\":\"harness_ok\",\"seq\":{_commandsProcessed},\"command\":{Json(command)}}}");
        }
        catch (Exception ex)
        {
            var msg = ex.GetType().Name + ": " + ex.Message;
            WriteOut($"{{\"event\":\"harness_raise_error\",\"seq\":{_commandsProcessed},\"command\":{Json(command)},\"error\":{Json(msg)}}}");
        }
    }

    void OnDumpState(DumpStateEvent e)
    {
        try
        {
            var snapshot = BuildStateSnapshot();
            File.WriteAllText(e.Path, snapshot);
            WriteOut($"{{\"event\":\"harness_state_dumped\",\"path\":{Json(e.Path)}}}");
        }
        catch (Exception ex)
        {
            var msg = ex.GetType().Name + ": " + ex.Message;
            WriteOut($"{{\"event\":\"harness_dump_error\",\"error\":{Json(msg)}}}");
        }
    }

    string BuildStateSnapshot()
    {
        var sb = new StringBuilder();
        sb.Append('{');

        var state = TryResolve<IGameState>();
        var party = TryResolve<IParty>();
        var time = state?.Time;
        sb.Append($"\"loaded\":{(state?.Loaded == true ? "true" : "false")},");
        sb.Append($"\"map\":{Json(state?.MapId.ToString() ?? "")},");
        sb.Append($"\"time\":{Json(time?.ToString("O") ?? "")},");

        // Party leader position
        var leader = party?.Leader;
        if (leader != null)
        {
            var pos = leader.GetPosition();
            sb.Append($"\"leader\":{Json(leader.Id.ToString())},");
            sb.Append($"\"x\":{pos.X.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"\"y\":{pos.Y.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"\"z\":{pos.Z.ToString(CultureInfo.InvariantCulture)},");
        }

        // Members + HP + SP + conditions
        sb.Append("\"party\":[");
        bool first = true;
        if (party != null)
        {
            foreach (var pm in party.StatusBarOrder)
            {
                if (pm?.Apparent?.Combat == null) continue;
                if (!first) sb.Append(',');
                first = false;
                var c = pm.Apparent.Combat;
                sb.Append('{');
                sb.Append($"\"id\":{Json(pm.Id.ToString())},");
                sb.Append($"\"hp\":{c.LifePoints?.Current ?? 0},");
                sb.Append($"\"hpMax\":{c.LifePoints?.Max ?? 0},");
                var sp = pm.Apparent.Magic?.SpellPoints;
                sb.Append($"\"sp\":{sp?.Current ?? 0},");
                sb.Append($"\"spMax\":{sp?.Max ?? 0},");
                sb.Append($"\"conditions\":{Json(c.Conditions.ToString())},");
                sb.Append($"\"level\":{pm.Apparent.Level}");
                sb.Append('}');
            }
        }
        sb.Append("],");

        sb.Append($"\"commandsProcessed\":{_commandsProcessed}");
        sb.Append('}');
        return sb.ToString();
    }

    void WriteOut(string jsonLine)
    {
        try
        {
            using var fs = new FileStream(_outPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(fs);
            writer.WriteLine(jsonLine);
        }
        catch (IOException)
        {
            // Swallow — output channel hiccups shouldn't kill the game.
        }
    }

    static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
