using EasyRazer.Config;
using EasyRazer.Core;
using EasyRazer.Devices;
using EasyRazer.Devices.Razer;
using EasyRazer.UI.Modern;

namespace EasyRazer.UI;

/// <summary>
/// "Learn keys": finds out which internal slot number each physical button uses.
///
/// 1. Reads every slot the device answers for (live layer only).
/// 2. Temporarily sets each slot to send a different test key (A, B, C…).
/// 3. You press the highlighted button; the test key that arrives says which slot it is.
/// 4. Puts every slot back exactly as it was. (Unplugging also undoes it, since only live memory is touched.)
/// </summary>
public sealed class LearnDialog : Form
{
    // Test keys: plain keys only, never Escape/Enter/modifiers, so nothing odd can happen.
    private static readonly byte[] Pool = Enumerable.Range(0x04, 26)   // A–Z
        .Concat(Enumerable.Range(0x1E, 10))                              // 1–0
        .Concat(Enumerable.Range(0x3A, 12))                              // F1–F12
        .Concat(Enumerable.Range(0x68, 12))                              // F13–F24
        .Concat(Enumerable.Range(0x59, 11))                              // numpad
        .Select(i => (byte)i).ToArray();

    private readonly ProfileEngine _engine;
    private readonly RazerKeymapDevice _device;
    private readonly DeviceLayoutView _view = new() { Dock = DockStyle.Fill };
    private readonly Label _prompt = new()
    {
        Dock = DockStyle.Top,
        Height = 56,
        Font = Ui.Font(12.5f, semibold: true),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
    };
    private readonly ModernButton _start = new() { Text = "Start", Kind = ButtonKind.Primary, Margin = new Padding(8, 0, 0, 0) };
    private readonly ModernButton _skip = new() { Text = "Skip this one", Enabled = false, Margin = new Padding(8, 0, 0, 0) };
    private readonly ModernButton _cancel = new() { Text = "Cancel", Kind = ButtonKind.Ghost, Margin = new Padding(8, 0, 0, 0) };
    private readonly KeyRecorder _recorder = new() { EscapeCancels = true };

    private IDisposable? _lock;
    private Dictionary<byte, byte[]> _original = new();
    private List<byte> _candidates = new();
    private readonly Dictionary<byte, byte> _usageToSlot = new();
    private readonly HashSet<byte> _touched = new();
    private int _batch = -1;
    private int _index;
    private readonly Dictionary<string, byte> _learned = new();
    private bool _busy;

    private readonly IReadOnlyList<ButtonDef> _targets;

    /// <param name="only">Learn just these buttons (e.g. one you just added); null = every button.</param>
    public LearnDialog(ProfileEngine engine, RazerKeymapDevice device, IReadOnlyList<ButtonDef>? only = null)
    {
        _targets = only ?? device.Buttons;
        _engine = engine;
        _device = device;
        Text = $"Learn keys — {device.DisplayName}";
        Size = new Size(820, 620);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        BackColor = Ui.Card;
        ForeColor = Ui.Text;
        Font = Ui.Font(10f);
        ShowInTaskbar = false;
        Ui.DarkChrome(this, Ui.Card);

        _view.Shapes = device.Layout;
        _view.Describe = id => (_learned.ContainsKey(id) ? "✓" : "", _learned.ContainsKey(id) ? BindingSource.Custom : BindingSource.Factory);

        _prompt.Text = "This finds out how your device numbers its buttons. Takes about a minute.\n" +
                       "Press Start, then press each button when it lights up yellow.";
        _prompt.ForeColor = Theme.Text;

        _start.Click += async (_, _) => await StartAsync();
        _skip.Click += (_, _) => Next();
        _cancel.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 14, 16, 14), BackColor = Ui.Sidebar };
        buttons.Controls.Add(_start);
        buttons.Controls.Add(_skip);
        buttons.Controls.Add(_cancel);

        Controls.Add(_view);
        Controls.Add(_prompt);
        Controls.Add(buttons);

        _recorder.Completed += OnKey;
        _recorder.Cancelled += Close;
        Deactivate += (_, _) => { if (_recorder.IsRecording) _recorder.Stop(); };
        Activated += (_, _) => { if (_index < _targets.Count && _batch >= 0 && !_busy) _recorder.Start(); };
        FormClosing += (_, e) =>
        {
            if (_busy) { e.Cancel = true; return; } // finishing a device write; try again in a moment
            Finish(save: _learned.Count > 0);
        };
    }

    private async Task StartAsync()
    {
        _start.Enabled = false;
        _busy = true;
        _prompt.Text = "Reading the device…";
        _lock = await _engine.LockDevicesAsync();

        _original = await Task.Run(() => _device.ScanSlots(RazerKeymapDevice.ProfileLive));
        // Never remap a mouse's left click (slot 1): you need it to click Skip/Cancel.
        _candidates = _original.Keys
            .Where(k => !(_device.Style == KeymapStyle.Mouse && k == 0x01))
            .OrderBy(k => k).ToList();
        if (_candidates.Count == 0)
        {
            _prompt.Text = "The device didn't answer. Close Synapse if it's running, then try again.\n(Details are in the log.)";
            _prompt.ForeColor = Theme.Recording;
            _busy = false;
            Release();
            return;
        }

        if (!await WriteBatchAsync(0)) return;
        _busy = false;
        _skip.Enabled = true;
        _index = -1;
        Next();
    }

    /// <summary>Point the next group of slots at test keys (and put the previous group back).</summary>
    private async Task<bool> WriteBatchAsync(int batch)
    {
        _busy = true;
        _prompt.Text = "Setting up test keys…";
        var slots = _candidates.Skip(batch * Pool.Length).Take(Pool.Length).ToList();
        string? problem = null;

        await Task.Run(() =>
        {
            RestoreTouched();
            _usageToSlot.Clear();
            for (var i = 0; i < slots.Count; i++)
            {
                if (!_device.WriteBlock(RazerKeymapDevice.ProfileLive, slots[i], RazerKeymapDevice.KeyBlock(Pool[i]), out var p))
                {
                    problem = p;
                    continue;
                }
                _touched.Add(slots[i]);
                _usageToSlot[Pool[i]] = slots[i];
            }
        });

        _batch = batch;
        _busy = false;
        if (_usageToSlot.Count == 0)
        {
            Log.Warn($"{_device.DisplayName}: learn couldn't write test keys: {problem}");
            _prompt.Text = "The device wouldn't take the test keys. Please send me the log (tray icon → Open log).";
            _prompt.ForeColor = Theme.Recording;
            return false;
        }
        return true;
    }

    private void Next()
    {
        _index++;
        if (_index >= _targets.Count)
        {
            _recorder.Stop();
            _view.RecordingButton = null;
            _skip.Enabled = false;
            _prompt.Text = $"Done — learned {_learned.Count} of {_targets.Count} buttons. Close this window to save.";
            _prompt.ForeColor = Theme.Custom;
            _cancel.Text = "Close";
            return;
        }
        var button = _targets[_index];
        _view.RecordingButton = button.Id;
        _view.SelectedButton = button.Id;
        _prompt.Text = $"Press  {button.Label}   ({_index + 1} of {_targets.Count})\nEsc = stop,  Skip = leave this one out";
        _prompt.ForeColor = Theme.Recording;
        _recorder.Start();
    }

    private async void OnKey(KeyAction pressed)
    {
        if (_index < 0 || _index >= _targets.Count) return;
        var button = _targets[_index];

        if (HidKeys.TryGetUsage(pressed.Key, out var usage) && _usageToSlot.TryGetValue(usage, out var slot))
        {
            _learned[button.Id] = slot;
            Log.Info($"{_device.DisplayName}: learned {button.Label} = slot {slot:X2}");
            _view.Invalidate();
            Next();
            return;
        }

        // Not one of this group's test keys: the button may be in the next group of slots.
        var batches = (_candidates.Count + Pool.Length - 1) / Pool.Length;
        if (batches > 1)
        {
            if (!await WriteBatchAsync((_batch + 1) % batches)) return;
            _prompt.Text = $"Press  {button.Label}  once more   ({_index + 1} of {_targets.Count})";
        }
        else
        {
            _prompt.Text = $"That wasn't recognized. Press  {button.Label}  again, or Skip.";
        }
        _prompt.ForeColor = Theme.Recording;
        _recorder.Start();
    }

    /// <summary>Put every slot we changed back to what it was.</summary>
    private void RestoreTouched()
    {
        foreach (var slot in _touched.ToList())
        {
            if (_original.TryGetValue(slot, out var block))
                _device.WriteBlock(RazerKeymapDevice.ProfileLive, slot, block, out _);
            _touched.Remove(slot);
        }
    }

    private void Finish(bool save)
    {
        _recorder.Dispose();
        RestoreTouched(); // a fraction of a second; done before the window goes away on purpose
        Release();

        if (save)
        {
            var s = _engine.Config.DeviceSettingsFor(_device.KindId);
            foreach (var (id, slot) in _learned)
            {
                s.LearnedSlots[id] = slot;
                if (_original.TryGetValue(slot, out var before))
                    s.LearnedDefaults[id] = Convert.ToHexString(before);
            }
            ConfigStore.Save(_engine.Config);
            Log.Info($"{_device.DisplayName}: saved {_learned.Count} learned button(s)");
            LearnedSlots = new Dictionary<string, byte>(_learned);
            _engine.Reapply();
        }
    }

    /// <summary>What was learned (empty if nothing).</summary>
    public IReadOnlyDictionary<string, byte> LearnedSlots { get; private set; } = new Dictionary<string, byte>();

    private void Release()
    {
        _lock?.Dispose();
        _lock = null;
    }
}
