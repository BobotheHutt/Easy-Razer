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
    /// <summary>
    /// When false (the default) keymap packets are only logged, never sent. Turn on only after the
    /// keymap protocol for this device has been confirmed with a USB capture (see docs/CAPTURE.md).
    /// </summary>
    public bool KeymapWritesEnabled { get; set; }

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
    /// <summary>
    /// Press once: the device starts clicking <see cref="KeyAction.Mouse"/> continuously.
    /// Press again: it stops. Runs on the device itself (onboard macro).
    /// </summary>
    AutoClickToggle,
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

    /// <summary>Clicks per second for <see cref="ActionType.AutoClickToggle"/>.</summary>
    public int ClicksPerSecond { get; set; } = 15;

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
