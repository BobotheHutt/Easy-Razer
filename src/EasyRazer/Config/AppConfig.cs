using System.Text.Json.Serialization;

namespace EasyRazer.Config;

public sealed class AppConfig
{
    public const string DefaultProfileName = "Default";

    /// <summary>Single static color applied to every device, as "#RRGGBB".</summary>
    public string Color { get; set; } = "#00FF00";

    public LightingSettings Lighting { get; set; } = new();

    /// <summary>Profiles. The one named <see cref="DefaultProfileName"/> is used when no game profile matches.</summary>
    public List<Profile> Profiles { get; set; } = new() { new Profile { Name = DefaultProfileName } };

    /// <summary>Per device-kind settings (keyed by device kind id, e.g. "tartarus-pro").</summary>
    public Dictionary<string, DeviceSettings> Devices { get; set; } = new();

    public bool PauseSwitching { get; set; }

    /// <summary>Download and install new versions automatically (installed version only).</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>When recording a bind, Escape cancels and can never be bound.</summary>
    public bool EscapeCancels { get; set; } = true;

    [JsonIgnore]
    public Profile DefaultProfile
    {
        get
        {
            var p = Profiles.FirstOrDefault(x => x.IsDefault);
            if (p == null)
            {
                p = new Profile { Name = DefaultProfileName };
                Profiles.Insert(0, p);
            }
            return p;
        }
    }

    public DeviceSettings DeviceSettingsFor(string kindId)
    {
        if (!Devices.TryGetValue(kindId, out var s))
        {
            s = new DeviceSettings();
            Devices[kindId] = s;
        }
        return s;
    }
}

public sealed class LightingSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Also use OpenRGB for devices Easy Razer can't light itself (optional, off by default).</summary>
    public bool UseOpenRgb { get; set; }

    /// <summary>Full path to OpenRGB.exe. When set, Easy Razer starts it hidden if it isn't running.</summary>
    public string OpenRgbPath { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6742;
}

public enum ActivationMode
{
    /// <summary>Profile is active only while the game window is in front.</summary>
    WhileFocused,
    /// <summary>Profile stays active as long as the game process is running, even when alt-tabbed.</summary>
    WhileRunning,
}

public sealed class Profile
{
    public string Name { get; set; } = "New profile";

    /// <summary>Executable file names (e.g. "eldenring.exe"), compared case-insensitively.</summary>
    public List<string> Games { get; set; } = new();

    public ActivationMode Activation { get; set; } = ActivationMode.WhileFocused;

    /// <summary>deviceKindId -> (buttonId -> action). Buttons not listed keep the device default.</summary>
    public Dictionary<string, Dictionary<string, KeyAction>> Bindings { get; set; } = new();

    [JsonIgnore]
    public bool IsDefault => string.Equals(Name, AppConfig.DefaultProfileName, StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, KeyAction> BindingsFor(string kindId)
    {
        if (!Bindings.TryGetValue(kindId, out var map))
        {
            map = new Dictionary<string, KeyAction>();
            Bindings[kindId] = map;
        }
        return map;
    }

    public bool MatchesExe(string exe) =>
        Games.Any(g => string.Equals(g, exe, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => IsDefault ? $"{Name} (desktop)" : Name;
}

public sealed class DeviceSettings
{
    /// <summary>Write keybinds to the device. When off, they're only written to the log.</summary>
    public bool KeymapWritesEnabled { get; set; } = true;

    /// <summary>Button ids learned with "Learn keys" (button id -> device slot), for devices without a built-in table.</summary>
    public Dictionary<string, byte> LearnedSlots { get; set; } = new();

    /// <summary>
    /// What each learned button did before Easy Razer touched it (hex function block). Used for
    /// "(default)" so it restores the device's own behavior instead of a guessed factory value.
    /// </summary>
    public Dictionary<string, string> LearnedDefaults { get; set; } = new();

    /// <summary>Overrides the Razer transaction id if auto-detection picks the wrong one. null = auto.</summary>
    public byte? TransactionId { get; set; }
}

public enum ActionType
{
    /// <summary>Button does what it does out of the box.</summary>
    Default,
    /// <summary>Button does nothing.</summary>
    Disabled,
    Key,
    Mouse,
}

public enum MouseAction
{
    Left,
    Right,
    Middle,
    Back,
    Forward,
    ScrollUp,
    ScrollDown,
    DpiUp,
    DpiDown,
}

public sealed class KeyAction
{
    public ActionType Type { get; set; } = ActionType.Default;

    /// <summary>System.Windows.Forms.Keys name of the main key, e.g. "F", "D1", "F13".</summary>
    public string? Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }

    public MouseAction Mouse { get; set; }

    /// <summary>
    /// Turbo: while the button is held, the device repeats the click/key this many times per second
    /// (1–20). null = off. Stored on the device itself.
    /// </summary>
    public int? TurboRate { get; set; }

    public bool SupportsTurbo => Type == ActionType.Key ||
        (Type == ActionType.Mouse && Mouse is MouseAction.Left or MouseAction.Right or MouseAction.Middle
            or MouseAction.Back or MouseAction.Forward);

    public KeyAction Clone() => (KeyAction)MemberwiseClone();

    public override string ToString() => Type switch
    {
        ActionType.Default => "(default)",
        ActionType.Disabled => "Disabled",
        ActionType.Mouse => $"Mouse: {Mouse}",
        ActionType.Key => string.Join(" + ",
            new[] { Ctrl ? "Ctrl" : null, Shift ? "Shift" : null, Alt ? "Alt" : null, Key }
                .Where(s => !string.IsNullOrEmpty(s))),
        _ => Type.ToString(),
    };
}
