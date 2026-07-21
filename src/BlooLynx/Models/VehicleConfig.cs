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

    /// <summary>ICE = Internal Combustion Engine, EV = Electric Vehicle.</summary>
    public string? EngineType { get; set; }
}
