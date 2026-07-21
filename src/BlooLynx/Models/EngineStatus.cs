namespace BlooLynx.Models;

public class EngineStatus
{
    public bool Ignition { get; set; }
    public bool? Charging { get; set; }
    public double Range { get; set; }
    public double? BatteryCharge12v { get; set; }
    public double? StateOfCharge { get; set; }
    public bool Accessory { get; set; }
    public EvPlugType? PluggedTo { get; set; }

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
