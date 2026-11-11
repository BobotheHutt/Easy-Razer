using System.Diagnostics;
using EasyRazer.Config;
using EasyRazer.Devices;
using EasyRazer.Devices.Razer;
using EasyRazer.Lighting;
using HidSharp;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace EasyRazer.Core;

/// <summary>
/// Decides which profile should be active and pushes it to the devices.
/// Lives on the UI thread; device I/O happens on a background task, one write at a time.
/// </summary>
public sealed class ProfileEngine : IDisposable
{
    private readonly ForegroundWatcher _watcher = new();
    private readonly LightingService _lighting = new();
    private readonly SemaphoreSlim _deviceGate = new(1, 1);
    private readonly SynchronizationContext _ui;

    private readonly Timer _debounce = new() { Interval = 250 };
    private readonly Timer _runningCheck = new() { Interval = 5000 };
    private readonly Timer _hotplug = new() { Interval = 2000 };

    private string? _pendingExe;
    private Profile? _active;
    private Profile? _lastRunningProfile; // most recent "while running" profile that was focused

    public AppConfig Config { get; private set; }
    private readonly List<IKeymapDevice> _devices = new() { new TartarusPro(), new NagaTrinity() };
    private readonly CynosaChroma _cynosa = new();
    private readonly List<GenericRazerLight> _otherLights = new();

    /// <summary>Devices with keybinds: the built-in ones plus any Razer mouse found automatically.</summary>
    public IReadOnlyList<IKeymapDevice> Devices => _devices;

    /// <summary>Devices whose color Easy Razer sets itself.</summary>
    public IEnumerable<ILightingDevice> LightingDevices =>
        _devices.OfType<ILightingDevice>().Append(_cynosa).Concat(_otherLights);

    /// <summary>
    /// Automatically found mice are left exactly as they are (they may hold binds from Synapse) until
    /// you set a bind for them in some profile.
    /// </summary>
    private bool Manages(IKeymapDevice d) =>
        d is not GenericRazerMouse ||
        Config.Profiles.Any(p => p.Bindings.TryGetValue(d.KindId, out var map) && map.Values.Any(a => a.Type != ActionType.Default));

    /// <summary>Raised on the UI thread when a new device was found automatically.</summary>
    public event Action? DevicesChanged;
    public LightingService Lighting => _lighting;
    public Profile? ActiveProfile => _active;

    /// <summary>Raised on the UI thread when the active profile changes (or is re-applied).</summary>
    public event Action<Profile>? ProfileApplied;
    /// <summary>Raised on the UI thread when device or lighting status text changes.</summary>
    public event Action? StatusChanged;

    public ProfileEngine(AppConfig config)
    {
        Config = config;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _watcher.ForegroundChanged += exe =>
        {
            _pendingExe = exe;
            _debounce.Stop();
            _debounce.Start();
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Evaluate(_pendingExe, force: false);
        };
        _runningCheck.Tick += (_, _) => Evaluate(ForegroundWatcher.CurrentExe(), force: false);
        _hotplug.Tick += async (_, _) =>
        {
            _hotplug.Stop();
            await OnHardwareChangedAsync();
        };

        DeviceList.Local.Changed += (_, _) => _ui.Post(_ => { _hotplug.Stop(); _hotplug.Start(); }, null);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public async Task StartAsync()
    {
        Log.Info("Easy Razer starting");
        await RefreshDevicesAsync();
        _ = _lighting.ApplyAsync(Config, LightingDevices).ContinueWith(_ => RaiseStatus());
        // Make sure the device's own memory holds the desktop binds, so it behaves even if we're not running.
        await WriteAsync(Config.DefaultProfile, persist: true);
        Evaluate(ForegroundWatcher.CurrentExe(), force: true);
    }

    /// <summary>Called by the settings window after the user saves.</summary>
    public async Task UpdateConfigAsync(AppConfig newConfig, bool colorChanged)
    {
        Config = newConfig;
        ConfigStore.Save(Config);
        Log.Info("Settings saved");

        if (colorChanged) _ = _lighting.ApplyAsync(Config, LightingDevices).ContinueWith(_ => RaiseStatus());
        await RefreshDevicesAsync();
        await WriteAsync(Config.DefaultProfile, persist: true);
        _active = null; // profile objects were replaced; force a re-apply
        Evaluate(ForegroundWatcher.CurrentExe(), force: true);
    }

    public void SetPaused(bool paused)
    {
        Config.PauseSwitching = paused;
        ConfigStore.Save(Config);
        Log.Info(paused ? "Automatic switching paused" : "Automatic switching resumed");
        Evaluate(ForegroundWatcher.CurrentExe(), force: true);
    }

    /// <summary>
    /// Hold exclusive access to the devices (e.g. while "Learn keys" runs) so profile switches don't
    /// write in between. Dispose the result to release.
    /// </summary>
    public async Task<IDisposable> LockDevicesAsync()
    {
        await _deviceGate.WaitAsync();
        return new Releaser(_deviceGate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) gate.Release(); }
    }

    /// <summary>Write the active profile again (e.g. after learning keys).</summary>
    public void Reapply() => Evaluate(ForegroundWatcher.CurrentExe(), force: true);

    public Task ReapplyLightingAsync() => _lighting.ApplyAsync(Config, LightingDevices).ContinueWith(_ => RaiseStatus());

    /// <summary>Pick the right profile for the given foreground exe and apply it if it changed.</summary>
    private void Evaluate(string? foregroundExe, bool force)
    {
        var target = Choose(foregroundExe);
        _runningCheck.Enabled = target.Activation == ActivationMode.WhileRunning && !target.IsDefault;

        if (!force && ReferenceEquals(target, _active)) return;

        var previous = _active;
        _active = target;
        if (previous != null && previous != target)
            Log.Info($"Profile: {previous.Name} -> {target.Name}" + (foregroundExe != null ? $" ({foregroundExe})" : ""));

        _ = ApplyAndNotifyAsync(target);
    }

    private async Task ApplyAndNotifyAsync(Profile target)
    {
        if (await WriteAsync(target, persist: false)) ProfileApplied?.Invoke(target);
    }

    private Profile Choose(string? exe)
    {
        if (Config.PauseSwitching) return Config.DefaultProfile;

        // 1. The program in front has its own profile.
        if (exe != null)
        {
            var match = Config.Profiles.FirstOrDefault(p => !p.IsDefault && p.MatchesExe(exe));
            if (match != null)
            {
                if (match.Activation == ActivationMode.WhileRunning) _lastRunningProfile = match;
                return match;
            }
        }

        // 2. A "keep while running" game is still open in the background.
        if (_lastRunningProfile != null && Config.Profiles.Contains(_lastRunningProfile) && IsAnyRunning(_lastRunningProfile))
            return _lastRunningProfile;
        _lastRunningProfile = null;

        // 3. Desktop.
        return Config.DefaultProfile;
    }

    private static bool IsAnyRunning(Profile p)
    {
        foreach (var game in p.Games)
        {
            var name = Path.GetFileNameWithoutExtension(game);
            var procs = Process.GetProcessesByName(name);
            var running = procs.Length > 0;
            foreach (var proc in procs) proc.Dispose();
            if (running) return true;
        }
        return false;
    }

    /// <summary>
    /// Push a profile to the devices. Returns false if it was skipped because a newer profile switch
    /// came in while waiting (fast alt-tabbing only writes the profile you end up on).
    /// </summary>
    private async Task<bool> WriteAsync(Profile profile, bool persist)
    {
        await _deviceGate.WaitAsync(); // resumes on the UI thread
        try
        {
            if (!persist && _active != null && !ReferenceEquals(profile, _active)) return false;

            // Work out everything on the UI thread; the background task only talks to hardware.
            var cfg = Config;
            var jobs = Devices.Where(d => d.IsConnected && Manages(d))
                .Select(d => (Device: d, Bindings: EffectiveBindings(profile, d.KindId), Settings: cfg.DeviceSettingsFor(d.KindId)))
                .ToList();

            await Task.Run(() =>
            {
                foreach (var (dev, bindings, settings) in jobs)
                {
                    try
                    {
                        dev.Apply(bindings, settings, persist);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"{dev.DisplayName}: applying '{profile.Name}' failed", ex);
                    }
                }
            });
            return true;
        }
        finally
        {
            _deviceGate.Release();
            RaiseStatus();
        }
    }

    /// <summary>Game profile binds, falling back to the Default profile's binds, then factory.</summary>
    private IReadOnlyDictionary<string, KeyAction> EffectiveBindings(Profile profile, string kindId)
    {
        var result = new Dictionary<string, KeyAction>(Config.DefaultProfile.BindingsFor(kindId));
        if (!profile.IsDefault)
            foreach (var (button, action) in profile.BindingsFor(kindId))
                if (action.Type != ActionType.Default)
                    result[button] = action;
        return result;
    }

    private async Task RefreshDevicesAsync()
    {
        await _deviceGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                foreach (var dev in Devices)
                {
                    try { dev.Refresh(Config.DeviceSettingsFor(dev.KindId)); }
                    catch (Exception ex) { Log.Error($"{dev.DisplayName}: detection failed", ex); }
                }
                try { _cynosa.Refresh(); }
                catch (Exception ex) { Log.Error($"{_cynosa.DisplayName}: detection failed", ex); }
                foreach (var light in _otherLights)
                {
                    try { light.Refresh(); }
                    catch (Exception ex) { Log.Error($"{light.DisplayName}: detection failed", ex); }
                }
            });

            // Any other Razer device: set it up for basic use (color; buttons for mice).
            var known = _devices.OfType<RazerKeymapDevice>().Select(d => d.ProductId)
                .Append(0x022A) // Cynosa Chroma
                .Concat(_otherLights.Select(l => l.ProductId))
                .ToHashSet();
            var (mice, lights) = await Task.Run(() =>
            {
                var newMice = new List<GenericRazerMouse>();
                var newLights = new List<GenericRazerLight>();
                try
                {
                    foreach (var found in RazerDiscovery.Scan(known))
                    {
                        Log.Info($"Found {found.Name} ({found.ProductId:X4}{(found.IsMouse ? ", mouse" : "")})");
                        if (found.IsMouse)
                        {
                            var m = new GenericRazerMouse(found);
                            m.Refresh(Config.DeviceSettingsFor(m.KindId));
                            newMice.Add(m);
                        }
                        else
                        {
                            var l = new GenericRazerLight(found);
                            l.Refresh();
                            newLights.Add(l);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Looking for other Razer devices failed", ex);
                }
                return (newMice, newLights);
            });
            if (mice.Count + lights.Count > 0)
            {
                _devices.AddRange(mice);
                _otherLights.AddRange(lights);
                DevicesChanged?.Invoke();
            }
        }
        finally
        {
            _deviceGate.Release();
            RaiseStatus();
        }
    }

    private async Task OnHardwareChangedAsync()
    {
        var keymapBefore = Devices.Where(d => d.IsConnected).Select(d => d.KindId).ToHashSet();
        var lightsBefore = LightingDevices.Where(d => d.IsConnected).Select(d => d.DisplayName).ToHashSet();
        await RefreshDevicesAsync();
        if (Devices.Any(d => d.IsConnected && !keymapBefore.Contains(d.KindId)))
        {
            Log.Info("Device plugged in, re-applying profile");
            if (_active != null) await WriteAsync(_active, persist: false);
        }
        if (LightingDevices.Any(d => d.IsConnected && !lightsBefore.Contains(d.DisplayName)))
            await ReapplyLightingAsync();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _ui.Post(async _ =>
        {
            Log.Info("Resumed from sleep, re-applying everything");
            await Task.Delay(5000); // USB devices take a moment to come back
            await RefreshDevicesAsync();
            await ReapplyLightingAsync();
            Evaluate(ForegroundWatcher.CurrentExe(), force: true);
        }, null);
    }

    /// <summary>
    /// On exit, put the desktop (Default) binds back in the devices' live memory so a game profile
    /// doesn't stay active after the app is closed. Waits at most a couple of seconds.
    /// </summary>
    public void RestoreDefaultForExit()
    {
        if (_active == null || _active.IsDefault) return;
        var cfg = Config;
        var jobs = Devices.Where(d => d.IsConnected && Manages(d))
            .Select(d => (Device: d, Bindings: EffectiveBindings(cfg.DefaultProfile, d.KindId), Settings: cfg.DeviceSettingsFor(d.KindId)))
            .ToList();
        if (!_deviceGate.Wait(2000)) return;
        try
        {
            var work = Task.Run(() =>
            {
                foreach (var (dev, bindings, settings) in jobs)
                {
                    try { dev.Apply(bindings, settings, persist: false); }
                    catch (Exception ex) { Log.Error($"{dev.DisplayName}: restoring desktop binds on exit failed", ex); }
                }
            });
            work.Wait(3000);
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    private void RaiseStatus() => _ui.Post(_ => StatusChanged?.Invoke(), null);

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _watcher.Dispose();
        _debounce.Dispose();
        _runningCheck.Dispose();
        _hotplug.Dispose();
    }
}
