using System.Text.Json;
using System.Text.Json.Nodes;
using BlooLynx.Models;

namespace BlooLynx;

public class Vehicle
{
    private readonly Client _client;

    internal Vehicle(VehicleConfig vehicleConfig, Client client)
    {
        VehicleConfig = vehicleConfig;
        _client = client;
    }

    public VehicleConfig VehicleConfig { get; }

    public async Task<Response<Odometer>> OdometerAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync(
            HttpMethod.Get, $"/ac/v2/enrollment/details/{_client.UserConfig.Username}", _client.BuildHeaders(VehicleConfig), null,
            cancellationToken).ConfigureAwait(false);

        return await ResponseFactory.FromHttpResponseAsync(response, ParseOdometer).ConfigureAwait(false);
    }

    private Odometer ParseOdometer(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var found = doc.RootElement.GetProperty("enrolledVehicleDetails").EnumerateArray()
            .First(item => item.GetProperty("vehicleDetails").GetProperty("vin").GetString() == VehicleConfig.Vin);

        return new Odometer
        {
            Value = found.GetProperty("vehicleDetails").GetProperty("odometer").GetDouble(),
            DistanceUnit = DistanceUnit.Unspecified,
        };
    }

    /// <summary>Always polls the vehicle modem directly; there is no caching on the API side.</summary>
    public async Task<Response<Location>> LocationAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/ac/v2/rcs/rfc/findMyCar", _client.BuildHeaders(VehicleConfig), null, cancellationToken)
            .ConfigureAwait(false);

        return await ResponseFactory.FromHttpResponseAsync(response, ParseLocation).ConfigureAwait(false);
    }

    private static Location ParseLocation(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var coord = root.GetProperty("coord");
        var speed = root.GetProperty("speed");

        return new Location
        {
            Latitude = coord.GetProperty("lat").GetDouble(),
            Longitude = coord.GetProperty("lon").GetDouble(),
            Altitude = coord.GetProperty("alt").GetDouble(),
            SpeedUnit = speed.GetProperty("unit").GetInt32(),
            SpeedValue = speed.GetProperty("value").GetDouble(),
            Heading = root.GetProperty("head").GetDouble(),
        };
    }

    public async Task<Response> StartClimateAsync(StartOptions options, CancellationToken cancellationToken = default)
    {
        var isEv = VehicleConfig.IsEV;
        var gen2Ev = isEv && VehicleConfig.Generation == "2";
        var startUrl = isEv ? "ac/v2/evc/fatc/start" : "ac/v2/rcs/rsc/start";

        var heatedFeatures = 0;
        if (options.HeatedFeatures != 0)
        {
            if (!AdvancedClimateValidator.ValidHeats.Contains(options.HeatedFeatures))
            {
                return Response.Failure(400, $"Heated feature value {options.HeatedFeatures} is not supported.");
            }

            heatedFeatures = options.HeatedFeatures;
        }

        JsonObject? seatClimateOptions = null;
        if (options.SeatClimateSettings is not null && !gen2Ev)
        {
            var result = new JsonObject();
            foreach (var (seat, status) in EnumerateSeatSettings(options.SeatClimateSettings))
            {
                if (AdvancedClimateValidator.ValidSeats.TryGetValue(seat, out var targetSeat) && AdvancedClimateValidator.ValidStatus.Contains(status))
                {
                    result[targetSeat] = status;
                }
            }

            if (result.Count > 0)
            {
                seatClimateOptions = result;
            }
        }

        var body = new JsonObject
        {
            ["Ims"] = 0,
            ["airCtrl"] = options.Hvac ? 1 : 0,
            ["airTemp"] = new JsonObject
            {
                ["unit"] = 1,
                ["value"] = options.Temperature.ToString(),
            },
            ["defrost"] = options.Defrost,
            ["heating1"] = heatedFeatures,
            ["username"] = _client.UserConfig.Username,
            ["vin"] = VehicleConfig.Vin,
        };

        if (!gen2Ev)
        {
            body["igniOnDuration"] = options.Duration;
            body["seatHeaterVentInfo"] = seatClimateOptions;
        }

        var content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        return await ExecuteActionAsync(HttpMethod.Post, startUrl, content, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<(string Seat, int Status)> EnumerateSeatSettings(SeatHeaterVentInfo settings)
    {
        if (settings.DriverSeat is { } d) yield return ("driverSeat", d);
        if (settings.PassengerSeat is { } p) yield return ("passengerSeat", p);
        if (settings.RearLeftSeat is { } rl) yield return ("rearLeftSeat", rl);
        if (settings.RearRightSeat is { } rr) yield return ("rearRightSeat", rr);
    }

    public Task<Response> StopClimateAsync(CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(HttpMethod.Post, "/ac/v2/rcs/rsc/stop", null, cancellationToken);

    /// <summary>Sends a control-endpoint request and returns its outcome, tagged with the service_type
    /// <see cref="WaitForCommandAsync"/> needs to poll for this specific command's completion.</summary>
    private async Task<Response> ExecuteActionAsync(
        HttpMethod method, string service, HttpContent? content, CancellationToken cancellationToken, string serviceType = "REMOTE_POLL")
    {
        var response = await _client.SendAsync(method, service, _client.BuildHeaders(VehicleConfig), content, cancellationToken).ConfigureAwait(false);
        return await ResponseFactory.FromHttpResponseAsync(response, serviceType).ConfigureAwait(false);
    }

    /// <summary>
    /// Polls <c>rmt/getRunningStatus</c> until the command that produced <paramref name="commandResponse"/> actually
    /// completes on the vehicle, instead of trusting the initial HTTP 200. No-ops (returns success immediately) if
    /// <paramref name="commandResponse"/> didn't carry a transaction id — e.g. it wasn't a command response, or the
    /// command failed before a transaction id was issued.
    /// </summary>
    /// <remarks>
    /// Every fire like this triggers the same push notification the official app would send, since it's hitting
    /// the same backend — polling for completion doesn't add extra notifications, but firing the underlying
    /// command does regardless of which client sends it.
    /// </remarks>
    public async Task<Response> WaitForCommandAsync(
        Response commandResponse, TimeSpan? pollInterval = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (commandResponse.TransactionId is null)
        {
            return Response.Success();
        }

        var interval = pollInterval ?? TimeSpan.FromSeconds(5);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        var serviceType = commandResponse.ServiceType ?? "REMOTE_POLL";

        while (true)
        {
            var headers = _client.BuildHeaders(VehicleConfig);
            headers["tid"] = commandResponse.TransactionId;
            headers["login_id"] = _client.UserConfig.Username;
            headers["service_type"] = serviceType;

            using var httpResponse = await _client.SendAsync(HttpMethod.Get, "/ac/v2/rmt/getRunningStatus", headers, null, cancellationToken)
                .ConfigureAwait(false);
            var result = await ResponseFactory.FromHttpResponseAsync(httpResponse, ParseRunningStatus).ConfigureAwait(false);

            if (!result.IsSuccessful)
            {
                return Response.Failure(result.ResponseCode, result.ErrorMessage!);
            }

            if (result.Data.Status == "SUCCESS")
            {
                return Response.Success(result.ResponseCode);
            }

            if (result.Data.Status == "ERROR")
            {
                return Response.Failure(result.ResponseCode, "Command reported an ERROR status.");
            }

            if (DateTime.UtcNow >= deadline)
            {
                return Response.Failure(408, "Timed out waiting for command completion.");
            }

            await Task.Delay(result.Data.NextPollingInterval ?? interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private readonly struct RunningStatus(string status, TimeSpan? nextPollingInterval)
    {
        public string Status { get; } = status;

        public TimeSpan? NextPollingInterval { get; } = nextPollingInterval;
    }

    private static RunningStatus ParseRunningStatus(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var nextPollingInterval = root.TryGetProperty("nextPollingInterval", out var n) &&
            n.ValueKind == JsonValueKind.String &&
            int.TryParse(n.GetString(), out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : (TimeSpan?)null;

        return new RunningStatus(status, nextPollingInterval);
    }

    public async Task<Response<Status>> StatusAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        var headers = _client.BuildHeaders(VehicleConfig, refresh);

        var response = await _client.SendAsync(HttpMethod.Get, "/ac/v2/rcs/rvs/vehicleStatus", headers, null, cancellationToken)
            .ConfigureAwait(false);

        return await ResponseFactory.FromHttpResponseAsync(response, ParseStatus).ConfigureAwait(false);
    }

    private static Status ParseStatus(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var raw = doc.RootElement.GetProperty("vehicleStatus");

        return new Status
        {
            ClosurePanels = new ClosurePanelStatus
            {
                HoodOpen = GetBool(raw, "hoodOpen"),
                TrunkOpen = GetBool(raw, "trunkOpen"),
                Locked = GetBool(raw, "doorLock"),
                OpenDoors = ParseOpenDoors(raw),
            },
            Climate = new ClimateStatus
            {
                Active = GetBool(raw, "airCtrlOn"),
                SteeringWheelHeat = GetInt(raw, "steerWheelHeat") != 0,
                RearWindowHeat = GetInt(raw, "sideBackWindowHeat") != 0,
                Defrost = GetBool(raw, "defrost"),
                TemperatureSetpoint = raw.TryGetProperty("airTemp", out var airTemp) && airTemp.TryGetProperty("value", out var tv)
                    ? tv.GetString()
                    : null,
                TemperatureUnit = raw.TryGetProperty("airTemp", out var airTemp2) && airTemp2.TryGetProperty("unit", out var tu)
                    ? (TemperatureUnit)tu.GetInt32()
                    : TemperatureUnit.Celsius,
            },
            DriveTrain = ParseDriveTrain(raw),
            LastUpdate = raw.TryGetProperty("dateTime", out var dt) && dt.ValueKind == JsonValueKind.String
                ? DateTime.TryParse(dt.GetString(), out var parsed) ? parsed : null
                : null,
        };
    }

    private static DriveTrainStatus ParseDriveTrain(JsonElement raw)
    {
        var (range, rangeUnit) = GetEvOrIceRange(raw);

        return new DriveTrainStatus
        {
            Ignition = GetBool(raw, "engine"),
            Accessory = GetBool(raw, "acc"),
            Range = range,
            RangeUnit = rangeUnit,
            FuelLevel = raw.TryGetProperty("fuelLevel", out var fuelLevel) ? fuelLevel.GetDouble() : null,
            Charging = raw.TryGetProperty("evStatus", out var ev) && ev.TryGetProperty("batteryCharge", out var bc) && bc.GetBoolean(),
            BatteryCharge12v = raw.TryGetProperty("battery", out var bat) && bat.TryGetProperty("batSoc", out var soc) ? soc.GetDouble() : null,
            StateOfCharge = raw.TryGetProperty("evStatus", out var ev2) && ev2.TryGetProperty("batteryStatus", out var bs) ? bs.GetDouble() : null,
            PluggedTo = raw.TryGetProperty("evStatus", out var ev3) && ev3.TryGetProperty("batteryPlugin", out var bp) && bp.ValueKind == JsonValueKind.Number
                ? (EvPlugType)bp.GetInt32()
                : null,
            EstimatedCurrentChargeDuration = GetRemainTimeMinutes(raw, "atc"),
            EstimatedFastChargeDuration = GetRemainTimeMinutes(raw, "etc1"),
            EstimatedPortableChargeDuration = GetRemainTimeMinutes(raw, "etc2"),
            EstimatedStationChargeDuration = GetRemainTimeMinutes(raw, "etc3"),
            TirePressureWarningLamp = ParseTirePressureLamp(raw),
        };
    }

    private static double? GetRemainTimeMinutes(JsonElement vehicleStatus, string key) =>
        vehicleStatus.TryGetProperty("evStatus", out var ev) &&
        ev.TryGetProperty("remainTime2", out var remainTime2) &&
        remainTime2.TryGetProperty(key, out var entry) &&
        entry.TryGetProperty("value", out var val)
            ? val.GetDouble()
            : null;

    private static (double Value, DistanceUnit Unit) GetEvOrIceRange(JsonElement vehicleStatus)
    {
        if (vehicleStatus.TryGetProperty("evStatus", out var ev) &&
            ev.TryGetProperty("drvDistance", out var drv) &&
            drv.ValueKind == JsonValueKind.Array &&
            drv.GetArrayLength() > 0)
        {
            var first = drv[0];
            if (first.TryGetProperty("rangeByFuel", out var rbf) &&
                rbf.TryGetProperty("totalAvailableRange", out var tar) &&
                tar.TryGetProperty("value", out var val))
            {
                var value = val.GetDouble();
                if (value != 0)
                {
                    var unit = tar.TryGetProperty("unit", out var tarUnit) ? (DistanceUnit)tarUnit.GetInt32() : DistanceUnit.Unspecified;
                    return (value, unit);
                }
            }
        }

        if (vehicleStatus.TryGetProperty("dte", out var dte) && dte.TryGetProperty("value", out var dteVal))
        {
            var unit = dte.TryGetProperty("unit", out var dteUnit) ? (DistanceUnit)dteUnit.GetInt32() : DistanceUnit.Unspecified;
            return (dteVal.GetDouble(), unit);
        }

        return (0, DistanceUnit.Unspecified);
    }

    private static bool GetBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDouble() != 0,
            _ => false,
        };

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static OpenDoors ParseOpenDoors(JsonElement raw)
    {
        if (!raw.TryGetProperty("doorOpen", out var d))
        {
            return new OpenDoors();
        }

        return new OpenDoors
        {
            FrontRight = GetInt(d, "frontRight") != 0,
            FrontLeft = GetInt(d, "frontLeft") != 0,
            BackLeft = GetInt(d, "backLeft") != 0,
            BackRight = GetInt(d, "backRight") != 0,
        };
    }

    private static TirePressureWarningLamp ParseTirePressureLamp(JsonElement raw)
    {
        if (!raw.TryGetProperty("tirePressureLamp", out var t))
        {
            return new TirePressureWarningLamp();
        }

        return new TirePressureWarningLamp
        {
            RearLeft = GetInt(t, "tirePressureWarningLampRearLeft") != 0,
            FrontLeft = GetInt(t, "tirePressureWarningLampFrontLeft") != 0,
            FrontRight = GetInt(t, "tirePressureWarningLampFrontRight") != 0,
            RearRight = GetInt(t, "tirePressureWarningLampRearRight") != 0,
            All = GetInt(t, "tirePressureWarningLampAll") != 0,
        };
    }

    public Task<Response> UnlockAsync(CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(HttpMethod.Post, "/ac/v2/rcs/rdo/on", BuildUserVinForm(), cancellationToken);

    public Task<Response> LockAsync(CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(HttpMethod.Post, "/ac/v2/rcs/rdo/off", BuildUserVinForm(), cancellationToken);

    private FormUrlEncodedContent BuildUserVinForm() =>
        new(new Dictionary<string, string>
        {
            ["userName"] = _client.UserConfig.Username ?? string.Empty,
            ["vin"] = VehicleConfig.Vin,
        });

    public Task<Response> FlashLightsAsync(CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(HttpMethod.Post, "/ac/v2/rcs/rhl/light", BuildUserVinJson(), cancellationToken, serviceType: "LIGHTS_ONLY");

    public Task<Response> FlashLightsAndHonkAsync(CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(HttpMethod.Post, "/ac/v2/rcs/rhl/hnl", BuildUserVinJson(), cancellationToken, serviceType: "HORN_AND_LIGHTS");

    private StringContent BuildUserVinJson()
    {
        var body = new JsonObject
        {
            ["userName"] = _client.UserConfig.Username,
            ["vin"] = VehicleConfig.Vin,
        };
        return new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
    }

    public Task<Response> StartChargeAsync(CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(HttpMethod.Post, "/ac/v2/evc/charge/start", null, cancellationToken);

    public Task<Response> StopChargeAsync(CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(HttpMethod.Post, "/ac/v2/evc/charge/stop", null, cancellationToken);

    /// <summary>
    /// Sets the target state-of-charge (as a percentage) for AC (Level 2 / slow) and DC (Level 3 / fast)
    /// charging. Confirmed live against a real account and cross-checked in the official app's UI — see
    /// apireference.md. Note: AC appears to only accept 10% increments and silently rounds rather than
    /// rejecting an in-between value; untested whether DC has the same restriction.
    /// </summary>
    public Task<Response> SetChargeLimitsAsync(int acTargetPercent, int dcTargetPercent, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["targetSOClist"] = new JsonArray
            {
                new JsonObject { ["plugType"] = 0, ["targetSOClevel"] = dcTargetPercent },
                new JsonObject { ["plugType"] = 1, ["targetSOClevel"] = acTargetPercent },
            },
        };

        var content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        return ExecuteActionAsync(HttpMethod.Post, "/ac/v2/evc/charge/targetsoc/set", content, cancellationToken);
    }
}