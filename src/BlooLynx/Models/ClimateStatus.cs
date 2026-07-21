namespace BlooLynx.Models;

public class ClimateStatus
{
    public bool Active { get; set; }
    public bool SteeringWheelHeat { get; set; }
    public bool RearWindowHeat { get; set; }
    public string? TemperatureSetpoint { get; set; }
    public TemperatureUnit TemperatureUnit { get; set; }
    public bool Defrost { get; set; }
}
