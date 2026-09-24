using Windows.Devices.Enumeration;
using Windows.Devices.Sensors;

namespace DispCtrl.Display;

/// <summary>An ambient light sensor Windows knows about.</summary>
public readonly record struct AmbientSensor(string Id, string Name);

/// <summary>
/// The light sensors unison can follow: usually one, in a laptop's bezel beside
/// the built-in panel.
/// </summary>
/// <remarks>
/// Windows' sensor stack, not DDC/CI: MCCS names an ambient light sensor switch
/// (0x66) but no reading, and the one external monitor measured here advertised
/// the switch and never implemented it. A monitor with its own sensor dims
/// itself, and says nothing of the room to anything else.
/// </remarks>
public static class AmbientSensors
{
    public static IReadOnlyList<AmbientSensor> List()
    {
        try
        {
            DeviceInformationCollection found = DeviceInformation.FindAllAsync(LightSensor.GetDeviceSelector()).AsTask().GetAwaiter().GetResult();
            return [.. found.Select(d => new AmbientSensor(d.Id, string.IsNullOrWhiteSpace(d.Name) ? "Ambient light sensor" : d.Name))];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>The chosen sensor, or Windows' default one when <paramref name="id"/> is empty.</summary>
    public static LightSensor? Open(string? id)
    {
        try
        {
            return string.IsNullOrEmpty(id) ? LightSensor.GetDefault() : LightSensor.FromIdAsync(id).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>One reading, for showing what the sensor sees now.</summary>
    public static double? ReadLux(string? id)
    {
        try { return Open(id)?.GetCurrentReading()?.IlluminanceInLux; }
        catch (Exception) { return null; }
    }
}
