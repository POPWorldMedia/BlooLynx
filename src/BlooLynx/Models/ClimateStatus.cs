namespace BlooLynx.Models;

public class ClimateStatus
{
    public bool Active { get; set; }
    public bool SteeringWheelHeat { get; set; }
    public bool RearWindowHeat { get; set; }
    public ClimateTemperature Temperature { get; set; }
    public bool Defrost { get; set; }
}
