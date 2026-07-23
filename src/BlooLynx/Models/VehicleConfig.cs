namespace BlooLynx.Models;

/// <summary>
/// Registration data pulled from the account's vehicle enrollment list, used to construct a vehicle instance.
/// </summary>
public class VehicleConfig
{
    public string Nickname { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    public string RegistrationDate { get; set; } = string.Empty;
    public string BrandIndicator { get; set; } = string.Empty;
    public string RegistrationId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Generation { get; set; } = string.Empty;
    public string ModelYear { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public string Trim { get; set; } = string.Empty;

    /// <summary>From <c>evStatus</c>: <c>"E"</c> is an electric vehicle, anything else (e.g. <c>"N"</c>) is not.</summary>
    public bool IsEV { get; set; }

    /// <summary>The odometer reading captured at enrollment time, if the account's enrollment list included one.</summary>
    public double? Odometer { get; set; }
}
