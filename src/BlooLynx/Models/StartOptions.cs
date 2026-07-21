namespace BlooLynx.Models;

public class StartOptions
{
    public bool Hvac { get; set; }
    public int Duration { get; set; } = 10;
    public int Temperature { get; set; } = 70;
    public bool Defrost { get; set; }
    public int HeatedFeatures { get; set; }
    public SeatHeaterVentInfo? SeatClimateSettings { get; set; }
}
