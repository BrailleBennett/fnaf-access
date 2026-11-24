using System.Text.Json;
using System.Text.Json.Serialization;

namespace fnaf_access.Config;

public enum GameAction
{
    // Office panning & navigation
    PanLeft,
    PanRight,
    MenuUp,
    MenuDown,
    MenuConfirm,

    // Office controls (sighted parity: only active when physically in view)
    ToggleLeftDoor,
    ToggleLeftLight,
    ToggleRightDoor,
    ToggleRightLight,
    ToggleMonitor,
    MuteCall,
    HonkNose,

    // Camera selection (only active while camera monitor is up)
    Cam1A,
    Cam1B,
    Cam1C,
    Cam2A,
    Cam2B,
    Cam3,
    Cam4A,
    Cam4B,
    Cam5,
    Cam6,
    Cam7,
    CamPrevious,
    CamNext,

    // On-demand sighted-parity status queries
    ReadPowerAndUsage,
    ReadTimeAndNight,
    ReadDoorStatus,
    ReadCurrentView,
    RepeatLastOrDescribe,

    // Companion controls
    TogglePassthrough,
    SpeakHelp
}

public sealed class KeybindConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Maps each GameAction to a friendly key name (e.g., "Left", "Q", "Space", "1", "-", "F8").
    /// </summary>
    public Dictionary<GameAction, string> Bindings { get; set; } = CreateDefaultBindings();

    public static Dictionary<GameAction, string> CreateDefaultBindings() => new()
    {
        // Office panning & menus
        [GameAction.PanLeft] = "Left",
        [GameAction.PanRight] = "Right",
        [GameAction.MenuUp] = "Up",
        [GameAction.MenuDown] = "Down",
        [GameAction.MenuConfirm] = "Enter",

        // Office controls (require control to be physically visible on screen)
        [GameAction.ToggleLeftDoor] = "Q",
        [GameAction.ToggleLeftLight] = "A",
        [GameAction.ToggleRightDoor] = "E",
        [GameAction.ToggleRightLight] = "D",
        [GameAction.ToggleMonitor] = "Space",
        [GameAction.MuteCall] = "M",
        [GameAction.HonkNose] = "H",

        // Cameras (while monitor is open)
        [GameAction.Cam1A] = "1",
        [GameAction.Cam1B] = "2",
        [GameAction.Cam1C] = "3",
        [GameAction.Cam2A] = "4",
        [GameAction.Cam2B] = "5",
        [GameAction.Cam3] = "6",
        [GameAction.Cam4A] = "7",
        [GameAction.Cam4B] = "8",
        [GameAction.Cam5] = "9",
        [GameAction.Cam6] = "0",
        [GameAction.Cam7] = "-",
        [GameAction.CamPrevious] = "Left",
        [GameAction.CamNext] = "Right",

        // Status queries
        [GameAction.ReadPowerAndUsage] = "G",
        [GameAction.ReadTimeAndNight] = "T",
        [GameAction.ReadDoorStatus] = "S",
        [GameAction.ReadCurrentView] = "V",
        [GameAction.RepeatLastOrDescribe] = "R",

        // Companion controls (avoiding NVDA/JAWS Insert/CapsLock modifiers)
        [GameAction.TogglePassthrough] = "F8",
        [GameAction.SpeakHelp] = "F1"
    };

    public static KeybindConfig LoadOrCreate(string path)
    {
        KeybindConfig config;

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<KeybindConfig>(json, JsonOptions) ?? new KeybindConfig();

            bool updated = false;
            Dictionary<GameAction, string> defaults = CreateDefaultBindings();
            foreach ((GameAction action, string keyName) in defaults)
            {
                if (!config.Bindings.ContainsKey(action))
                {
                    config.Bindings[action] = keyName;
                    updated = true;
                }
            }

            if (updated)
            {
                config.Save(path);
            }
        }
        else
        {
            config = new KeybindConfig();
            config.Save(path);
        }

        return config;
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public Dictionary<int, List<GameAction>> BuildVirtualKeyMap()
    {
        var map = new Dictionary<int, List<GameAction>>();
        foreach ((GameAction action, string keyName) in Bindings)
        {
            if (VirtualKeyTable.TryParse(keyName, out int vk))
            {
                if (!map.TryGetValue(vk, out List<GameAction>? list))
                {
                    list = [];
                    map[vk] = list;
                }

                list.Add(action);
            }
        }

        return map;
    }
}

public static class VirtualKeyTable
{
    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Back"] = 0x08,
        ["Backspace"] = 0x08,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Escape"] = 0x1B,
        ["Esc"] = 0x1B,
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Delete"] = 0x2E,

        // Top-row digits
        ["0"] = 0x30, ["D0"] = 0x30,
        ["1"] = 0x31, ["D1"] = 0x31,
        ["2"] = 0x32, ["D2"] = 0x32,
        ["3"] = 0x33, ["D3"] = 0x33,
        ["4"] = 0x34, ["D4"] = 0x34,
        ["5"] = 0x35, ["D5"] = 0x35,
        ["6"] = 0x36, ["D6"] = 0x36,
        ["7"] = 0x37, ["D7"] = 0x37,
        ["8"] = 0x38, ["D8"] = 0x38,
        ["9"] = 0x39, ["D9"] = 0x39,

        // Letters
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,

        // Function keys
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,

        // Punctuation
        [";"] = 0xBA, ["OemSemicolon"] = 0xBA,
        ["="] = 0xBB, ["OemPlus"] = 0xBB,
        [","] = 0xBC, ["OemComma"] = 0xBC,
        ["-"] = 0xBD, ["OemMinus"] = 0xBD,
        ["."] = 0xBE, ["OemPeriod"] = 0xBE,
        ["/"] = 0xBF, ["OemQuestion"] = 0xBF,
        ["`"] = 0xC0, ["OemTilde"] = 0xC0,
        ["["] = 0xDB, ["OemOpenBrackets"] = 0xDB,
        ["\\"] = 0xDC, ["OemPipe"] = 0xDC,
        ["]"] = 0xDD, ["OemCloseBrackets"] = 0xDD,
        ["'"] = 0xDE, ["OemQuotes"] = 0xDE,
    };

    public static bool TryParse(string keyName, out int vk)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            vk = 0;
            return false;
        }

        string trimmed = keyName.Trim();
        if (NameToVk.TryGetValue(trimmed, out vk))
        {
            return true;
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out vk))
        {
            return true;
        }

        vk = 0;
        return false;
    }
}
