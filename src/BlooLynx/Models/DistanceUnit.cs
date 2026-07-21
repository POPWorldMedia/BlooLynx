namespace BlooLynx.Models;

/// <summary>
/// Distance unit code as used across the API's distance-valued fields (odometer, range, etc.). Confirmed via
/// <c>hyundai_kia_connect_api</c>'s <c>DISTANCE_UNITS</c> lookup table (0=unspecified, 1=km, 2 and 3 both=mi —
/// this API apparently has two historical codes for miles). <see cref="Miles"/> (3) is additionally confirmed
/// independently, from real range values observed on a US account paired with unit 3; unit 2 has not been
/// independently observed, only sourced from the lookup table above.
/// </summary>
public enum DistanceUnit
{
    Unspecified = 0,
    Kilometers = 1,

    /// <summary>Also means miles, per the same source as the rest of this enum — just never independently observed.</summary>
    MilesAlternate = 2,

    Miles = 3,
}
