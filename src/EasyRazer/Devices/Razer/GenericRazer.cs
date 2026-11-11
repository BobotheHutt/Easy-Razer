using EasyRazer.Config;
using EasyRazer.Core;
using HidSharp;
using static EasyRazer.Devices.LayoutShape;

namespace EasyRazer.Devices.Razer;

/// <summary>A Razer device found on USB that Easy Razer has no specific module for.</summary>
public sealed record DiscoveredRazer(int ProductId, string Name, bool IsMouse);

/// <summary>Finds Razer devices that speak the Razer control protocol.</summary>
public static class RazerDiscovery
{
    private const uint UsageMouse = 0x0001_0002;

    public static List<DiscoveredRazer> Scan(ISet<int> skipProductIds)
    {
        var found = new List<DiscoveredRazer>();
        foreach (var group in DeviceList.Local.GetHidDevices(RazerHidLink.RazerVendorId).GroupBy(d => d.ProductID))
        {
            if (skipProductIds.Contains(group.Key)) continue;
            var interfaces = group.ToList();
            // Only devices with the 90-byte control channel can be configured.
            if (!interfaces.Any(SupportsControl)) continue;

            var name = interfaces.Select(SafeName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? $"Razer device {group.Key:X4}";
            var isMouse = interfaces.Any(i => HasUsage(i, UsageMouse)) && !name.Contains("Keyboard", StringComparison.OrdinalIgnoreCase);
            found.Add(new DiscoveredRazer(group.Key, name.Trim(), isMouse));
        }
        return found;
    }

    private static bool SupportsControl(HidDevice d)
    {
        try { return d.GetMaxFeatureReportLength() >= RazerReport.Length + 1; }
        catch { return false; }
    }

    private static string? SafeName(HidDevice d)
    {
        try { return d.GetProductName(); }
        catch { return null; }
    }

    private static bool HasUsage(HidDevice d, uint usage)
    {
        try
        {
            return d.GetReportDescriptor().DeviceItems.Any(i => i.Usages.GetAllValues().Contains(usage));
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Sets a static color on a Razer device without knowing its model: tries the common Razer color
/// commands once (from the OpenRazer driver) and keeps the ones the device accepts.
/// </summary>
internal sealed class LightingProbe
{
    private static readonly (string Name, Func<byte, byte, byte, byte, RazerReport> Build)[] Candidates =
    {
        ("backlight", (t, r, g, b) => RazerLighting.ExtendedStatic(t, 0x05, r, g, b)),
        ("logo", (t, r, g, b) => RazerLighting.ExtendedStatic(t, 0x04, r, g, b)),
        ("scroll wheel", (t, r, g, b) => RazerLighting.ExtendedStatic(t, 0x01, r, g, b)),
        ("all zones", (t, r, g, b) => RazerLighting.ExtendedStatic(t, 0x00, r, g, b)),
        ("classic matrix", (t, r, g, b) => RazerReport.Create(t, 0x03, 0x0A, 0x04, 0x06, r, g, b)),
    };

    private List<int>? _working;

    public void Reset() => _working = null;

    public bool Apply(RazerHidLink link, string deviceName, byte r, byte g, byte b)
    {
        if (_working == null)
        {
            _working = new List<int>();
            for (var i = 0; i < Candidates.Length; i++)
                if (link.Send(Candidates[i].Build(link.TransactionId, r, g, b)) is { Status: 0x02 })
                    _working.Add(i);
            Log.Info(_working.Count > 0
                ? $"{deviceName}: color works via {string.Join(", ", _working.Select(i => Candidates[i].Name))}"
                : $"{deviceName}: none of the known color commands worked");
            return _working.Count > 0;
        }

        var ok = _working.Count > 0;
        foreach (var i in _working)
            ok &= link.Send(Candidates[i].Build(link.TransactionId, r, g, b)) is { Status: 0x02 };
        return ok;
    }
}

/// <summary>Any Razer device that isn't a mouse: color only.</summary>
public sealed class GenericRazerLight : ILightingDevice
{
    private readonly LightingProbe _probe = new();
    private RazerHidLink? _link;

    public GenericRazerLight(DiscoveredRazer found)
    {
        ProductId = found.ProductId;
        DisplayName = found.Name;
    }

    public int ProductId { get; }
    public string DisplayName { get; }
    public bool IsConnected => _link != null;

    public void Refresh()
    {
        if (!RazerHidLink.IsPresent(ProductId))
        {
            _link?.Dispose();
            _link = null;
            _probe.Reset();
            return;
        }
        _link ??= RazerHidLink.Open(ProductId, null, 0x1F);
    }

    public bool SetStaticColor(byte r, byte g, byte b) =>
        _link != null && _probe.Apply(_link, DisplayName, r, g, b);
}

/// <summary>
/// Any Razer mouse: right/middle/back/forward use the button numbers shared across Razer mice
/// (validated on Basilisk and Naga models by OpenSnek). Every write is read back before trusting it.
/// </summary>
public sealed class GenericRazerMouse : RazerKeymapDevice
{
    private readonly LightingProbe _probe = new();

    public GenericRazerMouse(DiscoveredRazer found)
    {
        ProductId = found.ProductId;
        DisplayName = found.Name;
    }

    public override string KindId => $"razer-{ProductId:x4}";
    public override string DisplayName { get; }
    public override int ProductId { get; }
    public override KeymapStyle Style => KeymapStyle.Mouse;

    public override IReadOnlyList<ButtonDef> Buttons { get; } = new List<ButtonDef>
    {
        new("right", "Right click", 0x02, M(MouseAction.Right)),
        new("middle", "Wheel click", 0x03, M(MouseAction.Middle)),
        new("back", "Side back", 0x04, M(MouseAction.Back)),
        new("forward", "Side forward", 0x05, M(MouseAction.Forward)),
    };

    public override IReadOnlyList<LayoutShape> Layout { get; } = new List<LayoutShape>
    {
        Deco("Top", 0, -9, 44, 6, ShapeKind.Label),
        Deco("", 0, 0, 44, 80, ShapeKind.Outline),
        Deco("Left click (fixed)", 2, 2, 15, 34, ShapeKind.Fixed),
        Btn("right", "Right click", 27, 2, 15, 34),
        Btn("middle", "Wheel click", 18, 4, 8, 20),
        Deco("Side buttons", 56, -9, 34, 6, ShapeKind.Label),
        Deco("", 56, 6, 34, 40, ShapeKind.Outline),
        Btn("forward", "Forward", 59, 10, 28, 15),
        Btn("back", "Back", 59, 28, 28, 15),
    };

    protected override IEnumerable<RazerReport> StaticColorReports(byte tid, byte r, byte g, byte b) =>
        Array.Empty<RazerReport>(); // not used: color goes through the probe below

    public override bool SetStaticColor(byte r, byte g, byte b) =>
        Link != null && _probe.Apply(Link, DisplayName, r, g, b);
}
