namespace BlooLynx.Models;

public class DriveTrainStatus
{
    public bool Ignition { get; set; }
    public bool? Charging { get; set; }
    public double Range { get; set; }

    /// <summary>Unit for <see cref="Range"/>, from the same JSON node's <c>unit</c> field (<c>totalAvailableRange.unit</c>
    /// or <c>dte.unit</c>, whichever supplied <see cref="Range"/>).</summary>
    public DistanceUnit RangeUnit { get; set; }

    /// <summary>Gas tank percentage, from <c>vehicleStatus.fuelLevel</c>. Meaningful for ICE/PHEV vehicles;
    /// on a pure EV (no gas tank) this reads 0 and shouldn't be treated as a real fuel level.</summary>
    public double? FuelLevel { get; set; }
    public double? BatteryCharge12v { get; set; }
    public double? StateOfCharge { get; set; }
    public bool Accessory { get; set; }
    public EvPlugType? PluggedTo { get; set; }
    public TirePressureWarningLamp TirePressureWarningLamp { get; set; } = new();
    public TirePressure TirePressure { get; set; } = new();

    /// <summary>
    /// Estimated minutes remaining for each charge method, from <c>evStatus.remainTime2</c>. The
    /// atc/etc1/etc2/etc3 -> current/fast/portable/station mapping is inferred by analogy with Kia's
    /// equivalent (but differently-nested) remainChargeTime fields, not independently confirmed against
    /// documentation, so treat the specific method assignment with some skepticism even though the
    /// values themselves are confirmed real.
    /// </summary>
    public double? EstimatedCurrentChargeDuration { get; set; }
    public double? EstimatedFastChargeDuration { get; set; }
    public double? EstimatedPortableChargeDuration { get; set; }
    public double? EstimatedStationChargeDuration { get; set; }
}
