namespace BlooLynx.Models;

/// <summary>
/// Normalized vehicle status, remapped from the region-specific raw payload.
/// </summary>
public class Status
{
    public EngineStatus Engine { get; set; } = new();
    public ClimateStatus Climate { get; set; } = new();
    public ChassisStatus Chassis { get; set; } = new();
    public DateTime? LastUpdate { get; set; }
}
