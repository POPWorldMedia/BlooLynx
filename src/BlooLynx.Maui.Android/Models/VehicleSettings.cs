namespace BlooLynx.Maui.Android.Models;

/// <summary>
/// User-supplied, per-vehicle figures the API doesn't report anywhere — see <c>docs/apireference.md</c>, which
/// has no field for either. Keyed by VIN in secure storage by <see cref="Services.StateService"/>.
/// </summary>
public class VehicleSettings
{
    public double? BatteryCapacityKwh { get; set; }
    public double? EfficiencyMilesPerKwh { get; set; }
}
