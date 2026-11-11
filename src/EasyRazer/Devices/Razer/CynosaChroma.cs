using EasyRazer.Core;

namespace EasyRazer.Devices.Razer;

/// <summary>Razer Cynosa Chroma keyboard (USB 1532:022A). Lighting only.</summary>
public sealed class CynosaChroma : ILightingDevice
{
    private const int ProductId = 0x022A;
    private const byte PreferredTid = 0x3F; // what OpenRazer uses for this keyboard
    private RazerHidLink? _link;

    public string DisplayName => "Cynosa Chroma";
    public bool IsConnected => _link != null;

    public void Refresh()
    {
        if (!RazerHidLink.IsPresent(ProductId))
        {
            if (_link != null) Log.Info($"{DisplayName}: unplugged");
            _link?.Dispose();
            _link = null;
            return;
        }
        _link ??= RazerHidLink.Open(ProductId, null, PreferredTid);
    }

    public bool SetStaticColor(byte r, byte g, byte b) =>
        _link?.Send(RazerLighting.ExtendedStatic(_link.TransactionId, RazerLighting.BacklightLed, r, g, b)) is { Status: 0x02 };
}
