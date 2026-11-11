using EasyRazer.Config;

namespace EasyRazer.Devices;

/// <summary>A remappable button on a device.</summary>
/// <param name="Id">Stable id used in config.json (never change once released).</param>
/// <param name="Label">What the user sees.</param>
/// <param name="ProtocolId">Button id the firmware uses. null = learned on the device with "Learn keys".</param>
/// <param name="Factory">What the button does out of the box. Used for "(default)" bindings.</param>
public sealed record ButtonDef(string Id, string Label, byte? ProtocolId, KeyAction Factory);

/// <summary>
/// A device whose button mappings can be written to its own memory. Lighting is not part of this:
/// every device's color is handled by OpenRGB, so any device works for color without a module.
/// </summary>
public interface IKeymapDevice
{
    /// <summary>Id used in config.json, e.g. "tartarus-pro".</summary>
    string KindId { get; }
    string DisplayName { get; }
    IReadOnlyList<ButtonDef> Buttons { get; }

    /// <summary>Schematic drawing of the device used by the settings window.</summary>
    IReadOnlyList<LayoutShape> Layout { get; }

    bool IsConnected { get; }
    /// <summary>One-line human readable status for the Devices tab.</summary>
    string Status { get; }

    /// <summary>Find (or re-find) the hardware. Safe to call often.</summary>
    void Refresh(DeviceSettings settings);

    /// <summary>Write a full button map to the device. Buttons missing from <paramref name="bindings"/> get their factory action.</summary>
    void Apply(IReadOnlyDictionary<string, KeyAction> bindings, DeviceSettings settings, bool persist);
}
