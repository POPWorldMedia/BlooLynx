namespace BlooLynx.Models;

public class ClosurePanelStatus
{
    public bool HoodOpen { get; set; }
    public bool TrunkOpen { get; set; }
    public bool Locked { get; set; }
    public OpenDoors OpenDoors { get; set; } = new();
}
