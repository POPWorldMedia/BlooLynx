namespace BlooLynx.Models;

/// <summary>
/// Temperature unit code used by the API's <c>airTemp.unit</c> field. Confirmed via
/// <c>hyundai_kia_connect_api</c>'s <c>TEMPERATURE_UNITS</c> lookup table (0=Celsius, 1=Fahrenheit). In
/// practice, that same reference source doesn't even bother reading this field for the US region — it just
/// assumes Fahrenheit unconditionally, since the US API appears to always operate in Fahrenheit regardless
/// of what (if anything) this field varies to.
/// </summary>
public enum TemperatureUnit
{
    Celsius = 0,
    Fahrenheit = 1,
}
