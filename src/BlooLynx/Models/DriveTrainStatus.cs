namespace BlooLynx.Models;

public class DriveTrainStatus
{
    public bool Ignition { get; set; }
    public bool? IsCharging { get; set; }
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

    /// <summary>Estimated minutes remaining on the current charge session, from <c>evStatus.remainTime[0].value</c>.</summary>
    public double? EstimatedCurrentChargeDuration { get; set; }

    /// <summary>Target state-of-charge percentage for DC (fast) charging, from <c>evStatus.reservChargeInfos.targetSOClist</c>
    /// entry with <c>plugType == 0</c>.</summary>
    public double? ChargeLimitDc { get; set; }

    /// <summary>Target state-of-charge percentage for AC (portable/station) charging, from <c>evStatus.reservChargeInfos.targetSOClist</c>
    /// entry with <c>plugType == 1</c>.</summary>
    public double? ChargeLimitAc { get; set; }

    /// <summary>Instantaneous charging power in kW, from <c>evStatus.realTimePower</c>.</summary>
    public double? ChargingPowerKw { get; set; }
}
