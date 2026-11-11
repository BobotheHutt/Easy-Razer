using EasyRazer.Config;
using EasyRazer.Core;

namespace EasyRazer.Devices.Razer;

/// <summary>
/// Shared logic for Razer devices with onboard button mapping.
///
/// IMPORTANT: the keymap command below (<see cref="BuildKeymapReport"/>) is NOT yet confirmed against
/// real Synapse traffic. Until a device's <see cref="DeviceSettings.KeymapWritesEnabled"/> is turned on,
/// packets are only written to the log ("dry run"), so a wrong guess can't change anything on the device.
/// docs/CAPTURE.md explains how to record what Synapse sends so the details can be filled in.
/// </summary>
public abstract class RazerKeymapDevice : IKeymapDevice
{
    // ---- Keymap protocol (UNVERIFIED, to be confirmed by capture) ----
    protected const byte KeymapClass = 0x02;
    protected const byte KeymapCommand = 0x0D;
    protected const byte StorageLive = 0x00;       // device RAM: no flash wear, lost on unplug
    protected const byte StoragePersistent = 0x01; // onboard profile slot 1: survives unplug/reboot
    protected const byte ActionDisabled = 0x00;
    protected const byte ActionMouse = 0x01;
    protected const byte ActionKeyboard = 0x02;
    protected const byte ActionDpi = 0x06;
    protected const byte ActionAutoClickToggle = 0x03; // onboard macro, toggle playback

    private RazerHidLink? _link;
    private string _status = "Not detected";

    public abstract string KindId { get; }
    public abstract string DisplayName { get; }
    public abstract int ProductId { get; }
    public abstract IReadOnlyList<ButtonDef> Buttons { get; }
    public abstract IReadOnlyList<LayoutShape> Layout { get; }

    public bool IsConnected => _link != null;
    public string Status => _status;

    public void Refresh(DeviceSettings settings)
    {
        if (!RazerHidLink.IsPresent(ProductId))
        {
            if (_link != null) Log.Info($"{DisplayName}: unplugged");
            _link = null;
            _status = "Not detected";
            return;
        }

        if (_link != null) return; // already talking to it

        _link = RazerHidLink.Open(ProductId, settings.TransactionId);
        _status = _link == null
            ? "Plugged in, but not answering (close Synapse / Razer services and try again)"
            : $"Connected, firmware {_link.Firmware}" + (settings.KeymapWritesEnabled ? "" : " — keybinds in dry-run mode");
    }

    public void Apply(IReadOnlyDictionary<string, KeyAction> bindings, DeviceSettings settings, bool persist)
    {
        if (_link == null) return;

        var storage = persist ? StoragePersistent : StorageLive;
        var sent = 0;
        var unknown = new List<string>();

        foreach (var button in Buttons)
        {
            var action = bindings.TryGetValue(button.Id, out var a) && a.Type != ActionType.Default ? a : button.Factory;

            if (button.ProtocolId is not { } pid)
            {
                unknown.Add(button.Label);
                continue;
            }

            var report = BuildKeymapReport(_link.TransactionId, storage, pid, action);
            if (report == null)
            {
                Log.Warn($"{DisplayName}: can't encode '{action}' for {button.Label}, skipped");
                continue;
            }

            if (!settings.KeymapWritesEnabled)
            {
                Log.Info($"{DisplayName} [dry run] {button.Label} -> {action}: {report.Describe()}");
                continue;
            }

            var reply = _link.Send(report);
            if (reply is not { Status: 0x02 })
            {
                Log.Warn($"{DisplayName}: device rejected {button.Label} -> {action} (status {reply?.Status.ToString("X2") ?? "no answer"})");
                _status = "Last keybind write failed — see log";
                return;
            }
            sent++;
        }

        if (unknown.Count > 0)
            Log.Info($"{DisplayName}: {unknown.Count} button(s) waiting on USB capture, not sent: {string.Join(", ", unknown)}");
        if (settings.KeymapWritesEnabled)
        {
            Log.Info($"{DisplayName}: wrote {sent} button(s) to {(persist ? "onboard memory" : "live memory")}");
            _status = $"Connected, firmware {_link.Firmware}";
        }
    }

    /// <summary>
    /// Encodes one button mapping. Argument layout (UNVERIFIED):
    ///   storage, button id, hypershift(0), action type, parameter length, parameters...
    /// </summary>
    protected virtual RazerReport? BuildKeymapReport(byte tid, byte storage, byte buttonId, KeyAction action)
    {
        byte[] parameters;
        byte type;

        switch (action.Type)
        {
            case ActionType.Disabled:
                type = ActionDisabled;
                parameters = Array.Empty<byte>();
                break;
            case ActionType.Mouse when action.Mouse is MouseAction.DpiUp or MouseAction.DpiDown:
                type = ActionDpi;
                parameters = new byte[] { action.Mouse == MouseAction.DpiUp ? (byte)0x01 : (byte)0x02 };
                break;
            case ActionType.Mouse:
                type = ActionMouse;
                parameters = new byte[] { MouseCode(action.Mouse), 0x00 };
                break;
            case ActionType.AutoClickToggle:
            {
                // UNVERIFIED: real devices likely need the click macro uploaded first and the button
                // pointed at it; the capture of Synapse assigning your toggle macro will show how.
                type = ActionAutoClickToggle;
                var ms = (ushort)Math.Clamp(1000 / Math.Max(1, action.ClicksPerSecond), 1, 1000);
                parameters = new byte[] { MouseCode(action.Mouse), (byte)(ms >> 8), (byte)ms };
                break;
            }
            case ActionType.Key:
                if (!HidKeys.TryGetUsage(action.Key, out var usage)) return null;
                type = ActionKeyboard;
                parameters = new[] { HidKeys.ModifierMask(action.Ctrl, action.Shift, action.Alt), usage };
                break;
            default:
                return null;
        }

        var args = new List<byte> { storage, buttonId, 0x00, type, (byte)parameters.Length };
        args.AddRange(parameters);
        return RazerReport.Create(tid, KeymapClass, KeymapCommand, (byte)args.Count, args.ToArray());
    }

    protected static byte MouseCode(MouseAction m) => m switch
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
