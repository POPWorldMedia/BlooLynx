namespace BlooLynx.Models;

public class ChassisStatus
{
    public bool HoodOpen { get; set; }
    public bool TrunkOpen { get; set; }
    public bool Locked { get; set; }
    public OpenDoors OpenDoors { get; set; } = new();
    public TirePressureWarningLamp TirePressureWarningLamp { get; set; } = new();
}
