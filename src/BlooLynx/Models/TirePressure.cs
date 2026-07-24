namespace BlooLynx.Models;

/// <summary>Per-tire pressure readings, in PSI, from <c>vehicleStatus.tirePressure</c>. Null when the vehicle
/// didn't report a reading for that tire.</summary>
public class TirePressure
{
    public int? FrontLeft { get; set; }
    public int? FrontRight { get; set; }
    public int? RearLeft { get; set; }
    public int? RearRight { get; set; }
}
