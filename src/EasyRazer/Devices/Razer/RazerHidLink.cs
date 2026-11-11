using EasyRazer.Core;
using HidSharp;

namespace EasyRazer.Devices.Razer;

/// <summary>
/// Finds the control interface of a Razer device and exchanges 90-byte feature reports with it.
/// Uses plain HID feature reports through the normal Windows HID driver — no extra driver needed.
/// </summary>
public sealed class RazerHidLink : IDisposable
{
    public const int RazerVendorId = 0x1532;

    // Transaction ids seen across Razer devices. Probed in this order unless the config overrides it.
    private static readonly byte[] CandidateTransactionIds = { 0x1F, 0x3F, 0xFF, 0x9F };

    private readonly HidDevice _device;
    private readonly object _io = new();
    private HidStream? _stream;

    public byte TransactionId { get; }
    public string Firmware { get; }
    public string DevicePath => _device.DevicePath;

    private RazerHidLink(HidDevice device, byte transactionId, string firmware)
    {
        _device = device;
        TransactionId = transactionId;
        Firmware = firmware;
    }

    /// <summary>
    /// Looks at every HID interface with the given product id and returns the first one that answers
    /// a harmless "get firmware version" request (command 00:81). Returns null if the device isn't plugged in.
    /// </summary>
    public static RazerHidLink? Open(int productId, byte? forcedTransactionId, byte? preferredTransactionId = null)
    {
        var candidates = DeviceList.Local.GetHidDevices(RazerVendorId, productId)
            .Where(SupportsRazerReports)
            .ToList();

        var tids = forcedTransactionId.HasValue
            ? new[] { forcedTransactionId.Value }
            : CandidateTransactionIds.OrderBy(t => t == preferredTransactionId ? 0 : 1).ToArray();

        foreach (var dev in candidates)
        foreach (var tid in tids)
        {
            try
            {
                var reply = Exchange(dev, RazerReport.Create(tid, 0x00, 0x81, 0x02));
                if (reply is { Status: 0x02 })
                {
                    var fw = $"v{reply.Arguments[0]}.{reply.Arguments[1]}";
                    Log.Info($"Razer {productId:X4}: control interface found (tid {tid:X2}, firmware {fw})");
                    return new RazerHidLink(dev, tid, fw);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Razer {productId:X4}: probe failed on {dev.DevicePath} tid {tid:X2}: {ex.Message}");
            }
        }

        if (candidates.Count > 0)
            Log.Warn($"Razer {productId:X4}: found {candidates.Count} interface(s) but none answered. Is Synapse still running?");
        return null;
    }

    public static bool IsPresent(int productId) =>
        DeviceList.Local.GetHidDevices(RazerVendorId, productId).Any();

    /// <summary>
    /// Send a command and return the device's answer (null if it never answered).
    /// The connection stays open between commands; if it has gone stale (device replugged), it's
    /// reopened once and the command retried.
    /// </summary>
    public RazerReport? Send(RazerReport report)
    {
        lock (_io)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    _stream ??= OpenStream(_device);
                    if (_stream == null) return null;
                    return Exchange(_stream, report);
                }
                catch (Exception ex) when (attempt == 0)
                {
                    Log.Warn($"Razer {_device.ProductID:X4}: connection error ({ex.Message}), reopening");
                    CloseStream();
                }
                catch
                {
                    CloseStream();
                    throw;
                }
            }
            return null;
        }
    }

    private static bool SupportsRazerReports(HidDevice d)
    {
        try { return d.GetMaxFeatureReportLength() >= RazerReport.Length + 1; }
        catch { return false; }
    }

    private static HidStream? OpenStream(HidDevice dev)
    {
        if (!dev.TryOpen(out HidStream stream)) return null;
        stream.ReadTimeout = 500;
        stream.WriteTimeout = 500;
        return stream;
    }

    /// <summary>One-off exchange on a device we aren't holding open (used while probing).</summary>
    private static RazerReport? Exchange(HidDevice dev, RazerReport report)
    {
        using var stream = OpenStream(dev);
        return stream == null ? null : Exchange(stream, report);
    }

    private static RazerReport? Exchange(HidStream stream, RazerReport report)
    {
        // Feature report id 0 + 90 payload bytes.
        var outBuf = new byte[RazerReport.Length + 1];
        report.ToBytes().CopyTo(outBuf, 1);
        stream.SetFeature(outBuf);

        // The device needs a moment; it answers "busy" (0x01) until it's done (longer for onboard-memory writes).
        var inBuf = new byte[RazerReport.Length + 1];
        for (var attempt = 0; attempt < 40; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 1 : 5);
            stream.GetFeature(inBuf);
            var reply = RazerReport.Parse(inBuf.AsSpan(1));
            if (reply.Status != 0x01) return reply;
        }
        return null;
    }

    private void CloseStream()
    {
        try { _stream?.Dispose(); } catch { }
        _stream = null;
    }

    public void Dispose()
    {
        lock (_io) CloseStream();
    }
}
