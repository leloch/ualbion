using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace UAlbion.Game;

/// <summary>
/// Structured event trace sink for diff-against-SR analysis. Enabled via the --trace CLI flag.
/// Each emit appends one tab-separated record so traces can be processed with standard tools.
/// </summary>
public static class TraceLog
{
    static StreamWriter _writer;
    static readonly object Sync = new();

    public static bool Enabled => _writer != null;

    public static void Init(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        lock (Sync)
        {
            _writer?.Dispose();
            _writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
            _writer.WriteLine("# UAlbion trace log. Format: tick\\tkind\\tkey=value...");
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    /// <summary>Emit a structured trace record. kind is a short kind string (e.g. "attack", "cast", "move"). </summary>
    public static void Emit(string kind, params (string key, object value)[] fields)
    {
        if (_writer == null) return;
        var sb = new StringBuilder(64);
        sb.Append(Environment.TickCount.ToString(CultureInfo.InvariantCulture));
        sb.Append('\t');
        sb.Append(kind);
        if (fields != null)
        {
            foreach (var (k, v) in fields)
            {
                sb.Append('\t');
                sb.Append(k);
                sb.Append('=');
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
            }
        }
        lock (Sync)
        {
            _writer?.WriteLine(sb.ToString());
        }
    }
}
