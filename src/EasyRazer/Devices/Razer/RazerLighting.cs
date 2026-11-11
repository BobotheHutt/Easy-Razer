namespace EasyRazer.Devices.Razer;

/// <summary>
/// Static-color commands, as used by the OpenRazer driver (driver/razerchromacommon.c, razerkbd_driver.c,
/// razermouse_driver.c).
/// </summary>
public static class RazerLighting
{
    public const byte VarStore = 0x01;     // saved on the device
    public const byte BacklightLed = 0x05;

    /// <summary>
    /// "Extended matrix" static effect, class 0x0F id 0x02:
    /// args [storage, led, effect 0x01 = static, 0, 0, color count 1, r, g, b]. Used by the Tartarus Pro
    /// and Cynosa Chroma (backlight LED, saved on the device).
    /// </summary>
    public static RazerReport ExtendedStatic(byte tid, byte led, byte r, byte g, byte b) =>
        RazerReport.Create(tid, 0x0F, 0x02, 0x09, VarStore, led, 0x01, 0x00, 0x00, 0x01, r, g, b);

    /// <summary>
    /// Naga Trinity (and Naga Pro family): switch to custom-frame mode, then send one frame with the
    /// color for its three zones (logo, wheel, side plate). Not saved on the device, so it's re-sent at
    /// startup, after sleep and on reconnect.
    /// </summary>
    public static IEnumerable<RazerReport> NagaTrinityStatic(byte tid, byte r, byte g, byte b)
    {
        yield return RazerReport.Create(tid, 0x0F, 0x02, 0x06, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00);
        yield return RazerReport.Create(tid, 0x0F, 0x03, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x02, r, g, b, r, g, b, r, g, b);
    }
}
