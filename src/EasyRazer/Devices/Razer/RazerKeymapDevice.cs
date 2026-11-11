using EasyRazer.Config;
using EasyRazer.Core;

namespace EasyRazer.Devices.Razer;

/// <summary>How a device takes button-function commands.</summary>
public enum KeymapStyle
{
    /// <summary>Mice: set 02:0C / get 02:8C, args [profile, slot, hypershift, block(7)].</summary>
    Mouse,
    /// <summary>Analog keyboards/keypads: set 02:12 / get 02:92, args [profile, key, hypershift, actuation, release, block(7)].</summary>
    AnalogKeyboard,
    /// <summary>Other keyboards: set 02:0D / get 02:8D, args [profile, key, hypershift, block(7)].</summary>
    Keyboard,
}

/// <summary>
/// Shared logic for Razer devices with onboard button mapping.
///
/// Protocol, from public reverse-engineering work on real hardware:
///   - OpenSnek (github.com/gh123man/OpenSnek, docs/protocol/USB_PROTOCOL.md): mouse button functions
///     02:0C / 02:8C, Naga Pro slot table, function blocks incl. turbo and DPI.
///   - openrazer-win (github.com/GavinViney/openrazer-win): same mouse command on Basilisk V3.
///   - OpenRazer issue #2031: keyboard variants 02:0D (standard) and 02:12 (analog).
///
/// A "function block" is 7 bytes: [type, data length, data..., zero padding].
///   00 00            disabled
///   01 01 btn        mouse button (1 left, 2 right, 3 middle, 4 back, 5 forward, 9/10 scroll up/down)
///   02 02 mods key   keyboard key (HID usage) with modifier mask
///   06 01 01 / 02    DPI up / down
///   0D 04 mods key hi lo   keyboard key with turbo
///   0E 03 btn hi lo        mouse button with turbo
///
/// Profile byte: 0x00 = live layer (takes effect immediately, gone after unplug, no flash wear),
/// 0x01 = onboard profile 1 (stored permanently).
/// </summary>
public abstract class RazerKeymapDevice : IKeymapDevice
{
    public const byte ProfileLive = 0x00;
    public const byte ProfilePersistent = 0x01;

    private RazerHidLink? _link;
    private string _status = "Not detected";
    private bool _verified;
    // Last block written per (profile, slot), so unchanged buttons aren't sent again on every switch.
    private readonly Dictionary<(byte Profile, byte Slot), byte[]> _written = new();
    // Analog keyboards: the actuation/release bytes of each key, read from the device so we never change them.
    private readonly Dictionary<byte, (byte Actuation, byte Release)> _analogPoints = new();

    public abstract string KindId { get; }
    public abstract string DisplayName { get; }
    public abstract int ProductId { get; }
    public abstract KeymapStyle Style { get; }
    public abstract IReadOnlyList<ButtonDef> Buttons { get; }
    public abstract IReadOnlyList<LayoutShape> Layout { get; }

    /// <summary>True when the device has no built-in slot table and needs "Learn keys" first.</summary>
    public bool NeedsLearning(DeviceSettings s) => Buttons.Any(b => SlotFor(b, s) == null);

    public bool IsConnected => _link != null;
    public string Status => _status;

    public void Refresh(DeviceSettings settings)
    {
        if (!RazerHidLink.IsPresent(ProductId))
        {
            if (_link != null) Log.Info($"{DisplayName}: unplugged");
            _link?.Dispose();
            _link = null;
            _verified = false;
            _analogPoints.Clear();
            _written.Clear();
            _status = "Not detected";
            return;
        }

        if (_link != null) return; // already talking to it

        _link = RazerHidLink.Open(ProductId, settings.TransactionId);
        UpdateStatus(settings);
    }

    private void UpdateStatus(DeviceSettings settings, string? problem = null)
    {
        if (_link == null)
        {
            _status = "Plugged in, but not answering (close Synapse / Razer services and try again)";
            return;
        }
        _status = $"Connected, firmware {_link.Firmware}";
        if (problem != null) _status += " — " + problem;
        else if (NeedsLearning(settings)) _status += " — click \"Learn keys\" to enable keybinds";
        else if (!settings.KeymapWritesEnabled) _status += " — keybind writing is off";
    }

    /// <summary>Learned slot if "Learn keys" was run, otherwise the built-in one.</summary>
    public static byte? SlotFor(ButtonDef b, DeviceSettings s) =>
        s.LearnedSlots.TryGetValue(b.Id, out var slot) ? slot : b.ProtocolId;

    public void Apply(IReadOnlyDictionary<string, KeyAction> bindings, DeviceSettings settings, bool persist)
    {
        if (_link == null) return;
        if (NeedsLearning(settings) && Buttons.All(b => SlotFor(b, settings) == null))
        {
            UpdateStatus(settings);
            return;
        }

        // Only write permanently once a live write has been read back correctly on this device.
        if (persist && !_verified)
        {
            Apply(bindings, settings, persist: false);
            if (!_verified) return;
        }

        var profile = persist ? ProfilePersistent : ProfileLive;
        var written = 0;
        var unchanged = 0;

        foreach (var button in Buttons)
        {
            if (SlotFor(button, settings) is not { } slot) continue;

            byte[]? block;
            KeyAction action;
            if (bindings.TryGetValue(button.Id, out var a) && a.Type != ActionType.Default)
            {
                action = a;
                block = Encode(a);
            }
            else
            {
                action = button.Factory;
                // Prefer what the device itself had (recorded by "Learn keys") over the built-in guess.
                block = settings.LearnedDefaults.TryGetValue(button.Id, out var hex) && TryHex(hex, out var learned)
                    ? learned
                    : Encode(button.Factory);
            }
            if (block == null)
            {
                Log.Warn($"{DisplayName}: can't encode '{action}' for {button.Label}, skipped");
                continue;
            }

            if (!settings.KeymapWritesEnabled)
            {
                Log.Info($"{DisplayName} [writing off] {button.Label} -> {action}: {Convert.ToHexString(block)}");
                continue;
            }

            // For onboard memory, look at what's stored before writing, so starting the app every day
            // doesn't rewrite flash that already holds the right thing.
            if (persist && _verified && !_written.ContainsKey((profile, slot)) && ReadBlock(profile, slot) is { } stored)
                _written[(profile, slot)] = stored;

            if (_verified && _written.TryGetValue((profile, slot), out var last) && last.AsSpan().SequenceEqual(block))
            {
                unchanged++;
                continue;
            }

            if (!WriteBlock(profile, slot, block, out var problem))
            {
                Log.Warn($"{DisplayName}: {button.Label} -> {action} failed: {problem}");
                UpdateStatus(settings, "last keybind write failed, see log");
                return;
            }

            // Read the first write of the session back to make sure the device really took it.
            if (!_verified)
            {
                var back = ReadBlock(profile, slot);
                if (back == null || !back.AsSpan(0, 4).SequenceEqual(block.AsSpan(0, 4)))
                {
                    Log.Warn($"{DisplayName}: wrote {Convert.ToHexString(block)} to slot {slot:X2} but read back " +
                             $"{(back == null ? "nothing" : Convert.ToHexString(back))}");
                    UpdateStatus(settings, "keybinds didn't stick, see log");
                    return;
                }
                _verified = true;
                Log.Info($"{DisplayName}: keybind write verified by read-back");
            }
            written++;
        }

        if (settings.KeymapWritesEnabled)
        {
            if (written > 0)
                Log.Info($"{DisplayName}: wrote {written} button(s) to {(persist ? "onboard memory" : "live memory")}" +
                         (unchanged > 0 ? $", {unchanged} already up to date" : ""));
            UpdateStatus(settings);
        }
    }

    // ------------------------------------------------------------------ low level

    /// <summary>Read the 7-byte function block of one slot, or null if the device doesn't answer for it.</summary>
    public byte[]? ReadBlock(byte profile, byte slot)
    {
        if (_link == null) return null;
        var (cmd, size) = Style switch
        {
            KeymapStyle.AnalogKeyboard => ((byte)0x92, (byte)0x0C),
            KeymapStyle.Keyboard => ((byte)0x8D, (byte)0x0A),
            _ => ((byte)0x8C, (byte)0x0A),
        };
        var reply = _link.Send(RazerReport.Create(_link.TransactionId, 0x02, cmd, size, profile, slot, 0x00));
        if (reply is not { Status: 0x02 } || reply.Arguments[1] != slot) return null;

        var offset = 3;
        if (Style == KeymapStyle.AnalogKeyboard)
        {
            _analogPoints[slot] = (reply.Arguments[3], reply.Arguments[4]);
            offset = 5;
        }
        return reply.Arguments.AsSpan(offset, 7).ToArray();
    }

    public bool WriteBlock(byte profile, byte slot, byte[] block, out string problem)
    {
        problem = "";
        if (_link == null)
        {
            problem = "not connected";
            return false;
        }

        RazerReport report;
        if (Style == KeymapStyle.AnalogKeyboard)
        {
            if (!_analogPoints.ContainsKey(slot)) ReadBlock(profile, slot);
            var (act, rel) = _analogPoints.TryGetValue(slot, out var p) ? p : ((byte)0, (byte)0);
            var args = new byte[] { profile, slot, 0x00, act, rel }.Concat(block).ToArray();
            report = RazerReport.Create(_link.TransactionId, 0x02, 0x12, 0x0C, args);
        }
        else
        {
            var args = new byte[] { profile, slot, 0x00 }.Concat(block).ToArray();
            report = RazerReport.Create(_link.TransactionId, 0x02, Style == KeymapStyle.Keyboard ? (byte)0x0D : (byte)0x0C, 0x0A, args);
        }

        var reply = _link.Send(report);
        if (reply is { Status: 0x02 })
        {
            _written[(profile, slot)] = block.ToArray();
            // Writing the active onboard profile also changes the live layer, so forget what we
            // thought the live layer held for this slot.
            if (profile != ProfileLive) _written.Remove((ProfileLive, slot));
            return true;
        }
        _written.Remove((profile, slot));
        problem = $"device answered {reply?.Status.ToString("X2") ?? "nothing"} to {report.Describe()}";
        return false;
    }

    /// <summary>Ask every slot id what it does. Used by "Learn keys".</summary>
    public Dictionary<byte, byte[]> ScanSlots(byte profile)
    {
        var found = new Dictionary<byte, byte[]>();
        for (var slot = 1; slot <= 0xFF; slot++)
        {
            var block = ReadBlock(profile, (byte)slot);
            if (block != null) found[(byte)slot] = block;
        }
        Log.Info($"{DisplayName}: scan of profile {profile} found {found.Count} slot(s): " +
                 string.Join(" ", found.Select(kv => $"{kv.Key:X2}={Convert.ToHexString(kv.Value)}")));
        return found;
    }

    // ------------------------------------------------------------------ encoding

    public static byte[]? Encode(KeyAction action)
    {
        var b = new byte[7];
        switch (action.Type)
        {
            case ActionType.Disabled:
                return b;

            case ActionType.Mouse when action.Mouse is MouseAction.DpiUp or MouseAction.DpiDown:
                b[0] = 0x06; b[1] = 0x01; b[2] = action.Mouse == MouseAction.DpiUp ? (byte)0x01 : (byte)0x02;
                return b;

            case ActionType.Mouse:
                var btn = MouseCode(action.Mouse);
                if (btn == 0) return null;
                if (action.TurboRate is { } mt && action.SupportsTurbo)
                {
                    var (hi, lo) = TurboRaw(mt);
                    b[0] = 0x0E; b[1] = 0x03; b[2] = btn; b[3] = hi; b[4] = lo;
                }
                else
                {
                    b[0] = 0x01; b[1] = 0x01; b[2] = btn;
                }
                return b;

            case ActionType.Key:
                if (!HidKeys.TryGetUsage(action.Key, out var usage)) return null;
                var mods = HidKeys.ModifierMask(action.Ctrl, action.Shift, action.Alt);
                if (action.TurboRate is { } kt)
                {
                    var (hi, lo) = TurboRaw(kt);
                    b[0] = 0x0D; b[1] = 0x04; b[2] = mods; b[3] = usage; b[4] = hi; b[5] = lo;
                }
                else
                {
                    b[0] = 0x02; b[1] = 0x02; b[2] = mods; b[3] = usage;
                }
                return b;

            default:
                return null;
        }
    }

    private static bool TryHex(string hex, out byte[] block)
    {
        block = Array.Empty<byte>();
        try
        {
            block = Convert.FromHexString(hex);
            return block.Length == 7;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>A keyboard block that sends this HID key (used by "Learn keys").</summary>
    public static byte[] KeyBlock(byte usage) => new byte[] { 0x02, 0x02, 0x00, usage, 0, 0, 0 };

    /// <summary>HID key a block sends, if it's a plain keyboard block (both the 1- and 2-byte forms keep the key in byte 3).</summary>
    public static byte? KeyOf(byte[] block) => block[0] == 0x02 && block[1] is 0x01 or 0x02 ? block[3] : null;

    /// <summary>Turbo speed: raw 1 = 20 presses/s ... 255 = 1 press/s (OpenSnek's measured scale).</summary>
    private static (byte, byte) TurboRaw(int perSecond)
    {
        var pps = Math.Clamp(perSecond, 1, 20);
        var raw = (int)Math.Round(1 + (20 - pps) * 254.0 / 19.0);
        return ((byte)(raw >> 8), (byte)raw);
    }

    private static byte MouseCode(MouseAction m) => m switch
    {
        MouseAction.Left => 0x01,
        MouseAction.Right => 0x02,
        MouseAction.Middle => 0x03,
        MouseAction.Back => 0x04,
        MouseAction.Forward => 0x05,
        MouseAction.ScrollUp => 0x09,
        MouseAction.ScrollDown => 0x0A,
        _ => 0x00,
    };

    // Helpers for the factory tables in subclasses.
    protected static KeyAction K(Keys key) => new() { Type = ActionType.Key, Key = key.ToString() };
    protected static KeyAction M(MouseAction m) => new() { Type = ActionType.Mouse, Mouse = m };
}
