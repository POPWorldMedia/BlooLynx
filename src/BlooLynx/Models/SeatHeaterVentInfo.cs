namespace BlooLynx.Models;

/// <summary>
/// Per-seat heater/vent setting. Values: 0=Off, 1=On, 2=Low Cool, 3=Medium Cool, 4=High Cool, 5=Low Heat, 6=Medium Heat, 7=High Heat.
/// </summary>
public class SeatHeaterVentInfo
{
    public int? DriverSeat { get; set; }
    public int? PassengerSeat { get; set; }
    public int? RearLeftSeat { get; set; }
    public int? RearRightSeat { get; set; }
}
