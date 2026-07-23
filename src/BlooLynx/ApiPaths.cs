namespace BlooLynx;

/// <summary>Relative paths for the BlueLink API endpoints this client calls.</summary>
internal static class ApiPaths
{
    public static string OAuthToken => "/v2/ac/oauth/token";
    public static string OAuthTokenRefresh => "/v2/ac/oauth/token/refresh";
    public static string EnrollmentDetails => "/ac/v2/enrollment/details";
    public static string FindMyCar => "/ac/v2/rcs/rfc/findMyCar";
    public static string ClimateStartEv => "/ac/v2/evc/fatc/start";
    public static string ClimateStart => "/ac/v2/rcs/rsc/start";
    public static string ClimateStopEv => "/ac/v2/evc/fatc/stop";
    public static string ClimateStop => "/ac/v2/rcs/rsc/stop";
    public static string RunningStatus => "/ac/v2/rmt/getRunningStatus";
    public static string VehicleStatus => "/ac/v2/rcs/rvs/vehicleStatus";
    public static string Unlock => "/ac/v2/rcs/rdo/on";
    public static string Lock => "/ac/v2/rcs/rdo/off";
    public static string FlashLights => "/ac/v2/rcs/rhl/light";
    public static string FlashLightsAndHonk => "/ac/v2/rcs/rhl/hnl";
    public static string ChargeStart => "/ac/v2/evc/charge/start";
    public static string ChargeStop => "/ac/v2/evc/charge/stop";
    public static string ChargeTargetSocSet => "/ac/v2/evc/charge/targetsoc/set";
}
