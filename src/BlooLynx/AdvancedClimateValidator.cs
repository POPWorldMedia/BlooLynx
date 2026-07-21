namespace BlooLynx;

/// <summary>
/// Valid values for the advanced seat-heat/vent and steering-wheel/rear-window heat options.
/// </summary>
public static class AdvancedClimateValidator
{
    public static readonly IReadOnlyDictionary<string, string> ValidSeats = new Dictionary<string, string>
    {
        ["driverSeat"] = "drvSeatHeatState",
        ["passengerSeat"] = "astSeatHeatState",
        ["rearLeftSeat"] = "rlSeatHeatState",
        ["rearRightSeat"] = "rrSeatHeatState",
    };

    // 0=Off,1=On,2=Off,3=Low Cool,4=Medium Cool,5=High Cool,6=Low Heat,7=Medium Heat,8=High Heat
    public static readonly IReadOnlyList<int> ValidStatus = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    // 0=Off,1=Steering Wheel and Rear Window,2=Rear Window,3=Steering Wheel
    public static readonly IReadOnlyList<int> ValidHeats = new[] { 0, 1, 2, 3 };
}
