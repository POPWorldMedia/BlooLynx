namespace BlooLynx.Models;

/// <summary>The HVAC temperature setpoint read from <c>airTemp</c>. The API reports the sentinel string
/// <c>"OFF"</c> instead of a number when the setpoint isn't active; this collapses that into
/// <see cref="IsOn"/> being <c>false</c> and <see cref="Temperature"/> reading <c>0</c>, rather than callers
/// having to string-compare the raw value themselves.</summary>
public readonly struct ClimateTemperature
{
    public ClimateTemperature(bool isOn, double temperature, TemperatureUnit unit)
    {
        IsOn = isOn;
        Temperature = temperature;
        Unit = unit;
    }

    public bool IsOn { get; }

    /// <summary>Meaningless (always 0) when <see cref="IsOn"/> is <c>false</c>.</summary>
    public double Temperature { get; }

    public TemperatureUnit Unit { get; }
}
