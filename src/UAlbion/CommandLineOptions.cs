using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UAlbion.Config;
using Veldrid;

namespace UAlbion;

public sealed class CommandLineOptions
{
    public ExecutionMode Mode { get; }
    public GraphicsBackend Backend { get; }
    public bool DebugMenus { get; }
    public bool Mute { get; }
    public string TracePath { get; private set; }
    public bool TraceEnabled => !string.IsNullOrEmpty(TracePath);
    public bool NeedsEngine => Mode == ExecutionMode.Game;
    public bool StartupOnly { get; }
    public bool UseRenderDoc { get; }
    /// <summary>
    /// Directory used by the external testing harness (driver writes commands to in.jsonl,
    /// reads results / state snapshots from out.jsonl). Null disables the harness channel.
    /// Enable with <c>--harness &lt;dir&gt;</c>.
    /// </summary>
    public string HarnessPath { get; private set; }
    /// <summary>
    /// TCP port for the HTTP remote-control harness. When set, the game hosts an
    /// HttpListener on <c>http://localhost:&lt;port&gt;/</c> that lets external tooling
    /// drive the game (POST events, observe state, click UI elements by ID, capture
    /// screenshots). Activate via <c>--harness-http &lt;port&gt;</c>. Null disables.
    /// </summary>
    public int? HarnessHttpPort { get; private set; }
    public string[] ConvertFrom { get; }
    public string ConvertTo { get; }
    public Regex ConvertFilePattern { get; }

    public string[] Commands { get; }
    public string[] DumpIds { get; }
    public List<string> Mods { get; }
    public DumpFormats DumpFormats { get; } = DumpFormats.Json;
    public ISet<AssetType> DumpAssetTypes { get; }
    public string[] DumpLanguages { get; }

    static readonly HashSet<string> KnownArgs = new(StringComparer.Ordinal)
    {
        "--GAME", "--DUMP", "-D", "--ISO", "-ISO", "--CONVERT", "--BUILD", "-B",
        "-H", "--HELP", "/?", "HELP",
        "-GL", "--OPENGL", "-GLES", "--OPENGLES", "-VK", "--VULKAN", "-METAL", "--METAL", "-D3D", "--DIRECT3D",
        "--MENUS", "--NO-AUDIO", "-MUTE", "--MUTE", "--TRACE", "--STARTUPONLY", "-S", "--RENDERDOC", "-RD",
        "--HARNESS", "--HARNESS-HTTP", "-HH", "--COMMANDS", "-C", "--TYPE", "-T",
        "--ID", "-ID", "-IDS", "--IDS", "--LANGUAGES", "--LANG", "-L",
        "--FILES", "-F", "--FORMATS", "--FORMAT", "--MODS", "--MOD", "-MODS", "-MOD", "-M"
    };

    public CommandLineOptions(string[] args)
    {
        // Defaults
        Mode = ExecutionMode.Game;
        Backend = GraphicsBackend.Vulkan;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToUpperInvariant();

            if (!KnownArgs.Contains(arg))
            {
                // Don't fail silently: a stray token usually means an argument value lost its
                // quoting somewhere along the launch chain (e.g. PowerShell 5.1's Start-Process
                // doesn't quote ArgumentList entries containing spaces, turning
                // -mods "Albion HD" into -mods Albion HD and silently dropping the HD mod).
                Console.WriteLine($"Warning: unrecognised command-line argument \"{args[i]}\" was ignored");
                continue;
            }

            // Mode
            if (arg == "--GAME") Mode = ExecutionMode.Game;
            if (arg is "--DUMP" or "-D") Mode = ExecutionMode.DumpData;
            if (arg is "--ISO" or "-ISO") Mode = ExecutionMode.BakeIsometric;
            if (arg is "--CONVERT" or "--BUILD" or "-B")
            {
                if (i + 2 >= args.Length)
                    throw new FormatException("\"--convert\" requires two parameters: the mod to convert from and the mod to convert to");
                ConvertFrom = args[++i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                ConvertTo = args[++i];
                Mode = ExecutionMode.ConvertAssets;
            }

            if (arg is "-H" or "--HELP" or "/?" or "HELP")
            {
                DisplayUsage();
                Mode = ExecutionMode.Exit;
                return;
            }

            // Options
            if (arg is "-GL" or "--OPENGL") Backend = GraphicsBackend.OpenGL;
            if (arg is "-GLES" or "--OPENGLES") Backend = GraphicsBackend.OpenGLES;
            if (arg is "-VK" or "--VULKAN") Backend = GraphicsBackend.Vulkan;
            if (arg is "-METAL" or "--METAL") Backend = GraphicsBackend.Metal;
            if (arg is "-D3D" or "--DIRECT3D") Backend = GraphicsBackend.Direct3D11;

            if (arg == "--MENUS") DebugMenus = true;
            if (arg is "--NO-AUDIO" or "-MUTE" or "--MUTE") Mute = true;
            if (arg is "--TRACE")
            {
                // Optional argument: --trace <path>. Default to ualbion-trace.log if no path given.
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    TracePath = args[++i];
                else
                    TracePath = "ualbion-trace.log";
            }
            if (arg is "--STARTUPONLY" or "-S") StartupOnly = true;
            if (arg is "--RENDERDOC" or "-RD") UseRenderDoc = true;

            if (arg == "--HARNESS")
            {
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("\"--harness\" requires a directory argument");
                    Mode = ExecutionMode.Exit;
                    return;
                }
                HarnessPath = args[++i];
            }

            if (arg is "--HARNESS-HTTP" or "-HH")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var port) || port <= 0 || port > 65535)
                {
                    Console.WriteLine("\"--harness-http\" requires a TCP port (1..65535)");
                    Mode = ExecutionMode.Exit;
                    return;
                }
                i++;
                HarnessHttpPort = port;
            }

            if (arg is "--COMMANDS" or "-C")
            {
                i++;
                if (i == args.Length)
                {
                    Console.WriteLine("\"-c\" requires an argument specifying the commands to run");
                    Mode = ExecutionMode.Exit;
                    return;
                }

                Commands = args[i].Split(';').Select(x => x.Trim()).ToArray();
            }

            if (arg is "--TYPE" or "-T")
            {
                i++;
                if (i == args.Length)
                {
                    Console.WriteLine("\"-type\" requires an argument specifying the asset types to process");
                    Mode = ExecutionMode.Exit;
                    return;
                }

                DumpAssetTypes = new HashSet<AssetType>();
                foreach (var typeString in args[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!Enum.TryParse<AssetType>(typeString, true, out var type))
                    {
                        var message = @$"Unknown type ""{typeString}"" encountered.
Valid Types: {string.Join(" ", Enum.GetNames<AssetType>().OrderBy(x => x))}";

                        throw new FormatException(message);
                    }

                    DumpAssetTypes.Add(type);
                }
            }

            if (arg is "--ID" or "-ID" or "-IDS" or "--IDS")
            {
                i++;
                if (i == args.Length)
                {
                    Console.WriteLine("\"-id\" requires an argument specifying the ids to process");
                    Mode = ExecutionMode.Exit;
                    return;
                }
                DumpIds = args[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }

            if (arg is "--LANGUAGES" or "--LANG" or "-L")
            {
                i++;
                if (i == args.Length)
                {
                    Console.WriteLine("\"-l\" requires an argument specifying the languages to process");
                    Mode = ExecutionMode.Exit;
                    return;
                }
                DumpLanguages = args[i].ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }

            if (arg is "--FILES" or "-F")
            {
                i++;
                if (i == args.Length)
                {
                    Console.WriteLine("\"--files\" requires an argument specifying the regex to match against");
                    Mode = ExecutionMode.Exit;
                    return;
                }

                ConvertFilePattern = new Regex(args[i]);
            }

            if (arg is "--FORMATS" or "--FORMAT")
            {
                i++;
                if (i == args.Length)
                {
                    Console.WriteLine("\"--formats\" requires an argument specifying the formats to process");
                    Mode = ExecutionMode.Exit;
                    return;
                }
                DumpFormats = 0;
                foreach (var type in args[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    DumpFormats |= Enum.Parse<DumpFormats>(type, true);
            }

            if (arg is "--MODS" or "--MOD" or "-MODS" or "-MOD" or "-M")
            {
                i++;
                if (i == args.Length)
                {
                    Console.WriteLine("\"-mods\" requires an argument specifying the mods to load");
                    Mode = ExecutionMode.Exit;
                    return;
                }

                Mods = [.. args[i].Split(' ', StringSplitOptions.RemoveEmptyEntries)];

                // Quoting is easily lost when another process launches the game (e.g.
                // PowerShell 5.1's Start-Process doesn't quote ArgumentList entries containing
                // spaces), turning -mods "Albion HD" into -mods Albion HD. Rather than silently
                // dropping the extra mods, treat following non-flag tokens as part of the list.
                while (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    Mods.AddRange(args[++i].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
    }

    static void DisplayUsage()
    {
        var formats = string.Join(" ", 
            Enum.GetValues<DumpFormats>()
                .Select(x => x.ToString())
                .OrderBy(x => x));

        var dumpTypes = string.Join(" ",
            Enum.GetValues<AssetType>()
                .Select(x => x.ToString())
                .OrderBy(x => x));

        Console.WriteLine($@"UAlbion
Command Line Options:
(Note: To specify multiple values for any argument use double quotes and separate values with spaces)

Execution Mode:
    --game : Run the game normally (default)
    --help : Display this usage information (aliases: -h /? help)
    --dump : Dump assets to the data/exported directory (aliases: -d)
    --iso : Debugging mode for investigating issues with the export of isometric tiles for 3D maps.
    --convert <FromMod> <ToMod> : Convert all assets from one mod's asset formats to another (e.g. Base->Unpacked, Unpacked->Repacked etc) (aliases --build and -b)

Rendering Backend:
    --direct3d : Use Direct3D11 (aliases: -d3d)
    --opengl   : Use OpenGL     (aliases: -gl)
    --opengles : Use OpenGLES   (aliases: -gles)
    --metal    : Use Metal      (aliases: -metal)
    --vulkan   : Use Vulkan     (aliases: -vk)

Options:
    --commands <Commands> : Raise the given events (semicolon separated list) on startup (aliases: -c), e.g. -c ""new_game 110 60 30; add_party_member Rainer; simple_chest 1 HunterClanKey""
    --menus       : Show debug menus
    --no-audio    : Runs the game without audio
    --startuponly : Exit immediately after the first frame (for profiling startup time etc) (aliases: -s)
    --renderdoc   : Load the RenderDoc plugin on startup (aliases: -rd)
    --mods        : Override the default mod list (aliases: -m --mod)
    --harness <Dir> : Run with the external testing-harness channel. The driver writes one event-script command per line to <Dir>/in.jsonl; the game appends per-command outcome records (and harness state dumps) to <Dir>/out.jsonl. Use with `dump_state <path>` events to observe game state from outside.
    --harness-http <Port> : Run with an HTTP remote-control harness on http://localhost:<port>/ (aliases: -hh). Endpoints: GET /healthz /state /ui ; POST /event /event/raw /click /click/at /screenshot /quit. Localhost-only by default. Use for autonomous test agents, CI smoke runs that need to drive UI, etc.

Dump / Convert options:
    --formats <Formats> : Specifies the formats for the dumped data (defaults to JSON, valid formats: {formats})
    --ids <Ids>         : Dump only the specified asset ids (space separated list) (aliases: --id -id -ids)
    --type <Types>      : Dump specific types of game data (space separated list, valid types: {dumpTypes}) (aliases: -t)
    --files <Regex>     : Convert only the assets required for files in the target mod that match the given regular expression (aliases: -f)
");
    }
}