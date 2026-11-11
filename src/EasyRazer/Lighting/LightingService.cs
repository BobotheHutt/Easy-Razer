using System.Diagnostics;
using EasyRazer.Config;
using EasyRazer.Core;
using EasyRazer.Devices;
using OpenRGB.NET;
using Color = OpenRGB.NET.Color;

namespace EasyRazer.Lighting;

/// <summary>
/// Sets every device to one static color.
/// Razer devices Easy Razer knows (Naga Trinity, Tartarus Pro, Cynosa Chroma) are set directly over USB.
/// Optionally, OpenRGB (running hidden as a server) handles any other devices; it's off by default.
/// </summary>
public sealed class LightingService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Status { get; private set; } = "Not applied yet";

    public async Task ApplyAsync(AppConfig cfg, IEnumerable<ILightingDevice> devices)
    {
        var settings = cfg.Lighting;
        if (!settings.Enabled)
        {
            Status = "Lighting control is off";
            return;
        }
        if (!TryParseColor(cfg.Color, out var color))
        {
            Status = $"Invalid color '{cfg.Color}'";
            return;
        }

        var connected = devices.Where(d => d.IsConnected).ToList();
        await _gate.WaitAsync();
        try
        {
            var native = await Task.Run(() => ApplyNative(connected, color));
            Status = native;
            if (settings.UseOpenRgb)
            {
                try
                {
                    await Task.Run(() => ApplyCore(settings, color));
                    Status = native + "\nOpenRGB: " + Status;
                }
                catch (Exception ex)
                {
                    Status = native + "\nOpenRGB: couldn't reach it (is the path right?)";
                    Log.Error("OpenRGB lighting failed", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Status = "Lighting failed, see log";
            Log.Error("Lighting failed", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ApplyNative(List<ILightingDevice> devices, Color color)
    {
        if (devices.Count == 0) return "No supported Razer devices connected";
        var ok = new List<string>();
        var failed = new List<string>();
        foreach (var d in devices)
        {
            try
            {
                if (d.SetStaticColor(color.R, color.G, color.B)) ok.Add(d.DisplayName);
                else failed.Add(d.DisplayName);
            }
            catch (Exception ex)
            {
                failed.Add(d.DisplayName);
                Log.Warn($"Lighting: {d.DisplayName} failed: {ex.Message}");
            }
        }
        var text = ok.Count > 0 ? $"Set: {string.Join(", ", ok)}" : "";
        if (failed.Count > 0) text += (text.Length > 0 ? ". " : "") + $"Failed: {string.Join(", ", failed)} (see log)";
        Log.Info($"Lighting #{color.R:X2}{color.G:X2}{color.B:X2}: {text}");
        return text;
    }

    private void ApplyCore(LightingSettings settings, Color color)
    {
        using var client = ConnectWithStartup(settings);
        var devices = client.GetAllControllerData();
        var ok = new List<string>();
        var failed = new List<string>();

        for (var i = 0; i < devices.Length; i++)
        {
            var d = devices[i];
            try
            {
                if (SetStatic(client, i, d, color)) ok.Add(d.Name);
                else failed.Add(d.Name);
            }
            catch (Exception ex)
            {
                failed.Add(d.Name);
                Log.Warn($"Lighting: {d.Name} failed: {ex.Message}");
            }
        }

        Status = $"{ok.Count} device(s) set to {cfg(color)}" + (failed.Count > 0 ? $"; failed: {string.Join(", ", failed)}" : "");
        Log.Info($"Lighting: {Status}. Devices: {string.Join(", ", ok)}");

        static string cfg(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static bool SetStatic(OpenRgbClient client, int index, Device d, Color color)
    {
        var modes = d.Modes;

        var stat = Array.FindIndex(modes, m =>
            m.Name.Equals("Static", StringComparison.OrdinalIgnoreCase) &&
            m.ColorMode is ColorMode.ModeSpecific or ColorMode.PerLed);
        if (stat >= 0)
        {
            var mode = modes[stat];
            var count = mode.ColorMode == ColorMode.PerLed ? d.Leds.Length : Math.Max(1, (int)mode.ColorMin);
            if (mode.ColorMode == ColorMode.ModeSpecific && mode.Colors.Length > count) count = mode.Colors.Length;
            client.UpdateMode(index, stat, null, null, Enumerable.Repeat(color, count).ToArray());
            if (mode.ColorMode == ColorMode.PerLed && d.Leds.Length > 0)
                client.UpdateLeds(index, Enumerable.Repeat(color, d.Leds.Length).ToArray());
            return true;
        }

        if (d.Leds.Length > 0)
        {
            client.SetCustomMode(index);
            client.UpdateLeds(index, Enumerable.Repeat(color, d.Leds.Length).ToArray());
            return true;
        }

        Log.Warn($"Lighting: {d.Name} has no static/direct mode");
        return false;
    }

    private static OpenRgbClient ConnectWithStartup(LightingSettings s)
    {
        var client = new OpenRgbClient(s.Host, s.Port, "Easy Razer", false, 1000, 4);
        try
        {
            client.Connect();
            return client;
        }
        catch when (TryStartOpenRgb(s))
        {
            // OpenRGB scans devices on startup; give it time, retrying the connection.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                Thread.Sleep(1000);
                try
                {
                    client.Connect();
                    Thread.Sleep(2000); // let its device scan finish before asking for devices
                    return client;
                }
                catch
                {
                    // keep waiting
                }
            }
            client.Dispose();
            throw new TimeoutException("OpenRGB was started but its SDK server never answered");
        }
    }

    private static bool TryStartOpenRgb(LightingSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.OpenRgbPath) || !File.Exists(s.OpenRgbPath)) return false;
        if (Process.GetProcessesByName("OpenRGB").Length > 0) return true; // running, maybe still starting up

        Log.Info("Starting OpenRGB in the background");
        Process.Start(new ProcessStartInfo
        {
            FileName = s.OpenRgbPath,
            Arguments = $"--server --server-port {s.Port} --startminimized",
            WorkingDirectory = Path.GetDirectoryName(s.OpenRgbPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        return true;
    }

    public static bool TryParseColor(string? hex, out Color color)
    {
        color = new Color(0, 0, 0);
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        color = new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}
