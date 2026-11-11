namespace EasyRazer.Devices;

/// <summary>A device whose lighting Easy Razer sets directly (no OpenRGB needed).</summary>
public interface ILightingDevice
{
    string DisplayName { get; }
    bool IsConnected { get; }

    /// <summary>Set the whole device to one static color. Returns false if the device refused.</summary>
    bool SetStaticColor(byte r, byte g, byte b);
}
