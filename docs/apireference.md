# API reference

Every network-calling public method in this library, with the HTTP path it hits, the headers it adds beyond the common set, and the request/response bodies involved. Base URL for everything is `https://api.telematics.hyundaiusa.com`.

Pure local accessors that don't call the network (`Vehicle.Vin()`, `Name()`, `Nickname()`, `Id()`, `BrandIndicator()`) are omitted — they just read `VehicleConfig` fields already in memory.

## Method quick reference

| Method | What it actually does |
| --- | --- |
| `Client.LoginAsync()` | Authenticate with username/password, establish a session |
| `Client.LogoutAsync()` | Clear the local session (no server-side call) |
| `Client.IsAuthenticated` | Whether the client currently holds an access token — a local, no-network check |
| `Client.GetVehiclesAsync()` | List the vehicles on this account |
| `Vehicle.OdometerAsync()` | Get the current odometer reading |
| `Vehicle.LocationAsync()` | Get the vehicle's current GPS location |
| `Vehicle.StartClimateAsync(StartOptions)` | HVAC on/off, target temperature, defrost, seat heat, steering-wheel heat |
| `Vehicle.StopClimateAsync()` | Turns off whatever `StartClimateAsync` turned on |
| `Vehicle.StatusAsync(bool refresh = false)` | Get full vehicle status (doors, tires, climate, battery/EV state, etc.) |
| `Vehicle.LockAsync()` / `UnlockAsync()` | Lock/unlock the doors |
| `Vehicle.FlashLightsAsync()` / `FlashLightsAndHonkAsync()` | Flash the lights, or flash the lights and honk the horn together (no horn-only option exists) |
| `Vehicle.StartChargeAsync()` / `StopChargeAsync()` | Start/stop EV charging |
| `Vehicle.SetChargeLimitsAsync(int, int)` | Set AC/DC target charge percentages — fully confirmed live, including the `plugType` direction |
| `Vehicle.WaitForCommandAsync(Response, ...)` | Poll until a command above (lock, climate start, charge start, etc.) actually completes on the vehicle, instead of trusting the initial HTTP 200 |

## Live verification

`LoginAsync`, `GetVehiclesAsync`, `StatusAsync`, and `LocationAsync` were verified against a real account and vehicle (2026-07-21) via a script that reported only field presence/type, never actual values. All four returned HTTP 200 and every field this library currently reads was present with the expected JSON type — see the "Verified live" notes under each section below for the couple of small discrepancies found. `StartClimateAsync`/`StopClimateAsync`/`LockAsync`/`UnlockAsync`/`StartChargeAsync`/`StopChargeAsync` were **not** exercised, since they act on a real vehicle.

## Common headers

Most vehicle-scoped calls go through `Client.BuildHeaders(VehicleConfig, bool? refresh)`, which sends:

```
access_token: <Session.AccessToken>
client_id: m66129Bb-em93-SPAHYN-bZ91-am4540zp19920
Host: api.telematics.hyundaiusa.com
User-Agent: okhttp/3.12.0
registrationId: <VehicleConfig.RegistrationId>
gen: <VehicleConfig.Generation>
username: <BlueLinkClientConfig.Username>
vin: <VehicleConfig.Vin>
APPCLOUD-VIN: <VehicleConfig.Vin>
Language: 0
to: ISS
encryptFlag: false
from: SPA
brandIndicator: <VehicleConfig.BrandIndicator>
bluelinkservicepin: <BlueLinkClientConfig.Pin>
offset: <US Eastern UTC offset in hours>
REFRESH: "true" | "false"   (only present when explicitly passed; only vehicleStatus honors it)
```

Every call below that uses these is noted as "common headers", with only additions/exceptions called out.

---

## `Client.LoginAsync()`

`POST /v2/ac/oauth/token`

Headers:
```
User-Agent: PostmanRuntime/7.26.10
client_id: m66129Bb-em93-SPAHYN-bZ91-am4540zp19920
client_secret: v558o935-6nne-423i-baa8
```

Request body (JSON):
```json
{
  "username": "<BlueLinkClientConfig.Username>",
  "password": "<BlueLinkClientConfig.Password>"
}
```

Response body (JSON), only these fields are read:
```json
{
  "access_token": "string",
  "refresh_token": "string",
  "expires_in": "480"
}
```
Note: `expires_in` comes back as a **quoted numeric string**, not a JSON number — handled via `JsonNumberHandling.AllowReadingFromString` on `TokenResponse.ExpiresIn`.

**Verified live**: `access_token`, `refresh_token`, and `expires_in` (as a string) were all confirmed present with correct types. `ct_token` — present in the decompiled app's demo fixture — was **not** present in this account's real response; treat it as optional/account-dependent rather than assume it's always there.

On success, populates `Session.AccessToken`/`RefreshToken`/`TokenExpiresAt` and invokes the `saveSession` delegate if one was registered.

---

## Token refresh (private — `Client.RefreshAccessTokenAsync`, not directly callable)

Not part of the public API; documented here because it's an HTTP call worth understanding. Runs automatically before every authenticated request via `Client.SendAsync` — there's no way to trigger it manually, and no reason to, since it's a no-op unless the token is actually near expiry.

`POST /v2/ac/oauth/token/refresh`

Same headers as `LoginAsync`.

Request body:
```json
{
  "refresh_token": "<Session.RefreshToken>"
}
```

Response body: identical shape to `LoginAsync`.

Only actually fires if `Session.TokenExpiresAt` is within 60 seconds of now (or already past) and a refresh token exists; otherwise it's a no-op returning `Response.Success()`. Called automatically before every authenticated request via `Client.SendAsync`, guarded by a semaphore so only one refresh is ever in flight. On failure, clears the session (forcing a fresh `LoginAsync`) rather than retrying the dead refresh token indefinitely.

---

## `Client.LogoutAsync()`

No network call. Clears `Session.AccessToken`/`RefreshToken`/`TokenExpiresAt` locally and persists that cleared state via `saveSession` if registered. There is no server-side revocation call.

---

## `Client.GetVehiclesAsync()`

`GET /ac/v2/enrollment/details/{username}`

Headers:
```
client_id: m66129Bb-em93-SPAHYN-bZ91-am4540zp19920
Host: api.telematics.hyundaiusa.com
User-Agent: okhttp/3.12.0
payloadGenerated: <UTC now, "yyyyMMddHHmmss">
includeNonConnectedVehicles: Y
```
(plus `access_token`, applied by `SendAsync`)

No request body.

Response body (only fields read, per enrolled vehicle):
```json
{
  "enrolledVehicleDetails": [
    {
      "vehicleDetails": {
        "nickName": "string",
        "vin": "string",
        "enrollmentDate": "string",
        "brandIndicator": "string",
        "regid": "string",
        "vehicleGeneration": "string",
        "evStatus": "N | E"
      }
    }
  ]
}
```
`evStatus: "N"` maps to `EngineType: "ICE"`, `"E"` maps to `"EV"`. Returns `IReadOnlyList<Vehicle>`, each backed by a populated `VehicleConfig`.

**Verified live**: all fields above confirmed present with the expected types, including `evStatus` as a plain string. Worth noting as a naming trap: `evStatus` means something completely different on `StatusAsync`'s endpoint, where it's a large nested object (see below) — same JSON key, unrelated shape, depending which endpoint you're looking at.

---

## `Vehicle.OdometerAsync()`

`GET /ac/v2/enrollment/details/{username}` — same endpoint as `GetVehiclesAsync`, re-parsed to pull out just this VIN's odometer reading.

Headers: common headers.

No request body.

Response fields read (matched by `vin`):
```json
{
  "enrolledVehicleDetails": [
    { "vehicleDetails": { "vin": "string", "odometer": 12345.0 } }
  ]
}
```
Maps to `Odometer { Value, DistanceUnit }`. `DistanceUnit` (an enum — see `Models/DistanceUnit.cs`) is hardcoded to `DistanceUnit.Unspecified` (0); the odometer payload doesn't actually carry a unit field at all, so this is a library-invented placeholder, not something read from the API. The enum's other values (`Kilometers = 1`, `MilesAlternate = 2`, `Miles = 3`) come from `hyundai_kia_connect_api`'s `DISTANCE_UNITS` lookup table — `Miles` is additionally confirmed independently from real range figures seen elsewhere in the API (`dte`/`totalAvailableRange`), `MilesAlternate` (2) has not been independently observed.

---

## `Vehicle.LocationAsync()`

`GET /ac/v2/rcs/rfc/findMyCar`

Headers: common headers. Always polls the vehicle modem directly — no server-side caching.

No request body.

Response body (only top-level fields read; a newer `gpsDetail` wrapper duplicates the same data but isn't used):
```json
{
  "coord": { "lat": 0.0, "lon": 0.0, "alt": 0.0 },
  "speed": { "unit": 0, "value": 0.0 },
  "head": 0.0
}
```
Maps to `Location { Latitude, Longitude, Altitude, SpeedUnit, SpeedValue, Heading }`.

**Verified live**: `coord.{lat,lon,alt}`, `speed.{unit,value}`, and `head` all confirmed present with the expected types. The `gpsDetail` wrapper seen in the decompiled app's demo fixture was **not** present in this account's real response — another point in favor of only relying on the flat top-level fields, as this library already does.

---

## `Vehicle.StartClimateAsync(StartOptions)`

This is the "remote start" feature from the official app: it turns on the HVAC system (and, if requested, defrost, steering-wheel/rear-window heat, and seat heat/vent) at a target temperature you control via `StartOptions`.

`POST /ac/v2/evc/fatc/start` (EV) or `POST /ac/v2/rcs/rsc/start` (non-EV)

Headers: common headers.

Request body:
```json
{
  "Ims": 0,
  "airCtrl": 0,
  "airTemp": { "unit": 1, "value": "70" },
  "defrost": false,
  "heating1": 0,
  "username": "string",
  "vin": "string",
  "igniOnDuration": 10,
  "seatHeaterVentInfo": {
    "drvSeatHeatState": 0,
    "astSeatHeatState": 0,
    "rlSeatHeatState": 0,
    "rrSeatHeatState": 0
  }
}
```
Notes:
- `igniOnDuration` and `seatHeaterVentInfo` are omitted entirely for gen-2 EVs.
- `airTemp.unit` is always hardcoded to `1` (Fahrenheit); there is no working Celsius path on this endpoint (confirmed against the real app's own climate-start payload, which does the same).
- `heating1` (steering wheel / rear window heat) and each seat's heat/vent level are validated against `AdvancedClimateValidator` before being sent; out-of-range values return `Response.Failure(400, ...)` locally without ever hitting the network.

No response body is parsed — success/failure comes from the HTTP status code only.

---

## `Vehicle.StopClimateAsync()`

Turns off whatever `StartClimateAsync` turned on (HVAC/defrost/seat heat/etc.).

`POST /ac/v2/rcs/rsc/stop`

Headers: common headers. No request body. No response body parsed.

---

## `Vehicle.StatusAsync(bool refresh = false)`

`GET /ac/v2/rcs/rvs/vehicleStatus`

Headers: common headers, plus `REFRESH: "true"`/`"false"` (from the `refresh` parameter) — this is the only endpoint that honors that header, forcing a live poll of the vehicle's modem instead of a cached reading.

No request body.

Response body (`vehicleStatus` object; only fields actually read are shown — the real payload carries substantially more, see the full contract below):
```json
{
  "vehicleStatus": {
    "hoodOpen": false,
    "trunkOpen": false,
    "doorLock": true,
    "doorOpen": { "frontLeft": 0, "frontRight": 0, "backLeft": 0, "backRight": 0 },
    "tirePressureLamp": {
      "tirePressureWarningLampFrontLeft": 0,
      "tirePressureWarningLampFrontRight": 0,
      "tirePressureWarningLampRearLeft": 0,
      "tirePressureWarningLampRearRight": 0,
      "tirePressureWarningLampAll": 0
    },
    "airCtrlOn": false,
    "steerWheelHeat": 0,
    "sideBackWindowHeat": 0,
    "defrost": false,
    "airTemp": { "value": "70", "unit": 1 },
    "engine": false,
    "acc": false,
    "evStatus": {
      "batteryCharge": false,
      "batteryStatus": 70,
      "drvDistance": [
        { "rangeByFuel": { "totalAvailableRange": { "value": 170 } } }
      ]
    },
    "battery": { "batSoc": 74 },
    "dte": { "value": 0 },
    "dateTime": "2022-06-26T16:49:15Z"
  }
}
```
Maps to `Status { Chassis, Climate, Engine, LastUpdate }`:
- `Chassis`: `HoodOpen`, `TrunkOpen`, `Locked` (from `doorLock`), `OpenDoors`, `TirePressureWarningLamp`.
- `Climate`: `Active` (from `airCtrlOn`), `SteeringWheelHeat`, `RearWindowHeat` (from `sideBackWindowHeat`), `Defrost`, `TemperatureSetpoint`/`TemperatureUnit` (from `airTemp`; `TemperatureUnit` is an enum, `Celsius = 0`/`Fahrenheit = 1`, confirmed via the same `hyundai_kia_connect_api` source — which notably doesn't bother reading this field itself for the US region, since that API appears to always operate in Fahrenheit regardless).
- `Engine`: `Ignition` (from `engine`), `Accessory` (from `acc`), `Range` (EV: `evStatus.drvDistance[0].rangeByFuel.totalAvailableRange.value`, falling back to ICE `dte.value` if that's zero/absent), `Charging` (from `evStatus.batteryCharge`), `BatteryCharge12v` (from `battery.batSoc`), `StateOfCharge` (from `evStatus.batteryStatus`), `PluggedTo` (from `evStatus.batteryPlugin`, as `EvPlugType?`), `EstimatedCurrentChargeDuration`/`EstimatedFastChargeDuration`/`EstimatedPortableChargeDuration`/`EstimatedStationChargeDuration` (from `evStatus.remainTime2.{atc,etc1,etc2,etc3}.value` respectively — see the caveat on `EngineStatus` in code: this specific atc/etc1/etc2/etc3 labeling is inferred by analogy with Kia's equivalent fields, not independently confirmed).
- `LastUpdate` from `dateTime`, parsed as `DateTime?` (`null` if missing/unparsable).

**Verified live**: every field this library reads was confirmed present with the expected type, including the newly-added `batteryPlugin` and `remainTime2.{atc,etc1,etc2,etc3}`.

**Also observed live but still unmapped** (real, populated fields seen in a live response that this library doesn't currently read — see the full contract below for the complete list): a second GPS location at `vehicleStatus.vehicleLocation.coord` (redundant with `findMyCar`), real per-tire PSI values under `tirePressure`, `windowOpen` state, a live (current, not just settable) `seatHeaterVentInfo`, and several EV fields (`v2G`, `wirelessCharging`, `chargePortDoorOpen`, `dischargingLimit`, a populated `reservChargeInfos.targetSOClist`, and a real charge-schedule/off-peak-power configuration).

### Full response contract (as observed live, 2026-07-21)

Every field seen in a real `vehicleStatus` response, not just the subset this library reads. Leaf values are replaced with a type placeholder rather than the real captured values (which included exact GPS coordinates, odometer, and tire pressures) — structure, field names, nesting, and types are all real; the example values are not. Fields already mapped by this library are marked **(mapped)**.

```jsonc
{
  "locationAccessInfo": "<string>",
  "hataTID": "<string>",
  "vehicleStatus": {
    "dateTime": "<string, ISO 8601>",                      // (mapped) -> Status.LastUpdate
    "acc": "<bool>",                                       // (mapped) -> Engine.Accessory
    "fuelLevel": "<int>",
    "defrostStatus": "<string \"true\"|\"false\">",        // string duplicate of defrost below
    "transCond": "<bool>",
    "tirePressure": {
      "tirePressureFrontLeft": "<int, PSI>",
      "tirePressureFrontRight": "<int, PSI>",
      "tirePressureRearLeft": "<int, PSI>",
      "tirePressureRearRight": "<int, PSI>",
      "datetimeFrontLeft": "<string, ISO 8601>",
      "datetimeFrontRight": "<string, ISO 8601>",
      "datetimeRearLeft": "<string, ISO 8601>",
      "datetimeRearRight": "<string, ISO 8601>"
    },
    "doorLockStatus": "<string \"true\"|\"false\">",        // string duplicate of doorLock below
    "doorOpen": {                                           // (mapped) -> Chassis.OpenDoors
      "frontLeft": "<int, 0|1>", "frontRight": "<int, 0|1>", "backLeft": "<int, 0|1>", "backRight": "<int, 0|1>"
    },
    "washerFluidStatus": "<bool>",
    "battery": {
      "batSoc": "<int>",                                   // (mapped) -> Engine.BatteryCharge12v
      "batState": "<int>",
      "sjbDeliveryMode": "<int>",
      "powerAutoCutMode": "<int>",
      "batSignalReferenceValue": { "batWarning": "<int>" }
    },
    "seatHeaterVentInfo": {                                 // live current state; unmapped (StartOptions only sends a desired state, never reads this back)
      "drvSeatHeatState": "<int>", "astSeatHeatState": "<int>", "rlSeatHeatState": "<int>", "rrSeatHeatState": "<int>"
    },
    "hazardStatus": "<int>",
    "vehicleLocation": { "coord": { "lat": "<double>", "lon": "<double>", "alt": "<double>", "type": "<double>" } }, // unmapped; redundant with findMyCar
    "ign3": "<bool>",
    "ignitionStatus": "<string \"true\"|\"false\">",
    "lowFuelLight": "<bool>",
    "sideBackWindowHeat": "<int>",                          // (mapped) -> Climate.RearWindowHeat
    "dte": { "unit": "<int>", "value": "<int>" },           // (mapped) -> Engine.Range (ICE fallback)
    "engine": "<bool>",                                     // (mapped) -> Engine.Ignition
    "hoodOpen": "<bool>",                                   // (mapped) -> Chassis.HoodOpen
    "breakOilStatus": "<bool>",
    "airConditionStatus": "<string \"true\"|\"false\">",    // string duplicate of airCtrlOn below
    "windowOpen": {                                         // unmapped
      "frontLeft": "<int>", "frontRight": "<int>", "backLeft": "<int>", "backRight": "<int>",
      "flOpenLevel": "<int>", "frOpenLevel": "<int>", "blOpenLevel": "<int>", "brOpenLevel": "<int>"
    },
    "smartKeyBatteryWarning": "<bool>",
    "steerWheelHeat": "<int>",                              // (mapped) -> Climate.SteeringWheelHeat
    "tailLampStatus": "<int>",
    "trunkOpen": "<bool>",                                  // (mapped) -> Chassis.TrunkOpen
    "trunkOpenStatus": "<string \"true\"|\"false\">",
    "doorLock": "<bool>",                                   // (mapped) -> Chassis.Locked
    "odometer": "<int>",
    "airCtrlOn": "<bool>",                                  // (mapped) -> Climate.Active
    "airTemp": { "unit": "<int>", "value": "<string>", "hvacTempType": "<int>" }, // (mapped, minus hvacTempType) -> Climate.TemperatureUnit/TemperatureSetpoint
    "evStatus": {
      "batteryCharge": "<bool>",                            // (mapped) -> Engine.Charging
      "batteryStatus": "<int>",                             // (mapped) -> Engine.StateOfCharge
      "batteryPlugin": "<int, 0-3>",                         // (mapped) -> Engine.PluggedTo
      "valueDiff": "<int>", "timeDiff": "<int>",
      "v2G": "<bool>", "wirelessCharging": "<bool>",
      "batteryPrecondition": "<bool>", "batteryDisCharge": "<bool>",
      "batteryDisChargePlugin": "<int>", "disChargeRemaintime": "<int>", "dischargingLimit": "<int>",
      "chargePortDoorOpen": "<int>",
      "remainTime": [ { "unit": "<int>", "value": "<int>" } ],
      "remainTime2": {                                      // (mapped) -> Engine.Estimated{Current,Fast,Portable,Station}ChargeDuration
        "atc": { "unit": "<int>", "value": "<int>" },
        "etc1": { "unit": "<int>", "value": "<int>" },
        "etc2": { "unit": "<int>", "value": "<int>" },
        "etc3": { "unit": "<int>", "value": "<int>" }
      },
      "drvDistance": [                                      // (mapped, totalAvailableRange only) -> Engine.Range
        { "type": "<int>", "rangeByFuel": {
            "totalAvailableRange": { "unit": "<int>", "value": "<int>" },
            "evModeRange": { "unit": "<int>", "value": "<int>" }
        } }
      ],
      "reservChargeInfos": {                                // unmapped: charge limits + schedule
        "targetSOClist": [
          { "plugType": "<int, 0=DC/1=AC>", "targetSOClevel": "<int, percent>",
            "dte": { "type": "<int>", "rangeByFuel": { "evModeRange": { "unit": "<int>", "value": "<int>" } } } }
        ],
        "reservFlag": "<int>",
        "ect": {
          "start": { "day": "<int>", "time": { "timeSection": "<int>", "time": "<string, HHmm>" } },
          "end": { "day": "<int>", "time": { "timeSection": "<int>", "time": "<string, HHmm>" } }
        },
        "reservChargeInfo": {
          "dateTime": "<string>",
          "reservChargeInfoDetail": {
            "reservChargeSet": "<bool>",
            "reservFatcSet": { "defrost": "<bool>", "airCtrl": "<int>", "airTemp": { "unit": "<int>", "value": "<string|null>" } },
            "reservInfo": { "day": ["<int, 0-6>"], "time": { "timeSection": "<int>", "time": "<string, HHmm>" } }
          }
        },
        "reserveChargeInfo2": "<same shape as reservChargeInfo>",
        "offpeakPowerInfo": {
          "offPeakPowerFlag": "<int>",
          "offPeakPowerTime1": { "startTime": { "timeSection": "<int>", "time": "<string>" }, "endTime": { "timeSection": "<int>", "time": "<string>" } }
        }
      }
    },
    "lampWireStatus": {
      "headLamp": { "headLampStatus": "<bool>", "leftHighLamp": "<bool>", "rightHighLamp": "<bool>", "leftLowLamp": "<bool>", "rightLowLamp": "<bool>", "leftBifuncLamp": "<bool>", "rightBifuncLamp": "<bool>" },
      "stopLamp": { "stopLampStatus": "<bool>", "leftLamp": "<bool>", "rightLamp": "<bool>" },
      "turnSignalLamp": { "turnSignalLampStatus": "<bool>", "leftFrontLamp": "<bool>", "rightFrontLamp": "<bool>", "leftRearLamp": "<bool>", "rightRearLamp": "<bool>" }
    },
    "sleepModeCheck": "<bool>",
    "defrost": "<bool>",                                    // (mapped) -> Climate.Defrost
    "tirePressureLamp": {                                   // (mapped) -> Chassis.TirePressureWarningLamp
      "tirePressureWarningLampFrontLeft": "<int, 0|1>", "tirePressureWarningLampFrontRight": "<int, 0|1>",
      "tirePressureWarningLampRearLeft": "<int, 0|1>", "tirePressureWarningLampRearRight": "<int, 0|1>",
      "tirePressureWarningLampAll": "<int, 0|1>"
    }
  }
}
```

Fields not marked `(mapped)` above are read by nothing in this library today — they're documented here purely because they were observed live, as a reference for future work.

---

## `Vehicle.UnlockAsync()` / `Vehicle.LockAsync()`

`POST /ac/v2/rcs/rdo/on` (unlock) / `POST /ac/v2/rcs/rdo/off` (lock)

Headers: common headers.

Request body: `application/x-www-form-urlencoded`, **not** JSON:
```
userName=<BlueLinkClientConfig.Username>&vin=<VehicleConfig.Vin>
```

No response body parsed.

---

## `Vehicle.FlashLightsAsync()` / `Vehicle.FlashLightsAndHonkAsync()`

`POST /ac/v2/rcs/rhl/light` (lights only) / `POST /ac/v2/rcs/rhl/hnl` (horn and lights together)

Headers: common headers.

Request body (JSON, **not** form-encoded — unlike `Lock`/`Unlock`):
```json
{
  "userName": "<BlueLinkClientConfig.Username>",
  "vin": "<VehicleConfig.Vin>"
}
```

No response body parsed.

**Confidence note**: paths and body shape come from `hyundai_kia_connect_api` (the same source that confirmed the charge endpoints), and are independently corroborated by the official app's own UI, which exposes exactly these two options — "flash lights" and "horn and lights" — with no separate horn-only control.

**Verified live** (2026-07-21): both fired for real. `FlashLightsAsync` returned HTTP 200 with an empty body, but a genuine `tmsTid` transaction-id response header — the first live confirmation of the transaction-id pattern. `FlashLightsAndHonkAsync`, fired immediately after, got HTTP **502** with a real rate-limit error (see "Error response shape" below) because the previous command was still in flight; retried ~20 seconds later and got HTTP 200 with its own `tmsTid`. Practical implication: **do not fire two remote commands back-to-back** — wait for the previous one to resolve (or at least a several-second buffer) or expect a 502.

---

## `Vehicle.StartChargeAsync()` / `Vehicle.StopChargeAsync()`

`POST /ac/v2/evc/charge/start` / `POST /ac/v2/evc/charge/stop`

Headers: common headers. No request body. No response body parsed.

**Confidence note**: unlike the other endpoints above (all traceable to the community `bluelinky` project, which this library mirrors closely), these two paths come from a different, more actively maintained project (`hyundai_kia_connect_api`) rather than from `bluelinky` itself — `bluelinky`'s own attempt at NA charge control was never resolved. Treat these as the best currently-available answer, not exhaustively confirmed against a live account under real charging conditions.

**Verified live** (2026-07-21, vehicle unplugged): both calls returned HTTP 200 with a **completely empty response body** — no error, no acknowledgment payload of any kind. This confirms the paths are at least accepted by the server, but since the vehicle wasn't plugged in and nothing came back to indicate outcome, this does **not** confirm the command actually does anything when a vehicle is plugged in — that would need testing against an actively charging vehicle to know for sure.

---

## `Vehicle.SetChargeLimitsAsync(int acTargetPercent, int dcTargetPercent)`

Sets the target state-of-charge (as a percentage) for AC (Level 2 / slow) and DC (Level 3 / fast) charging — closes the "charge limits" gap called out earlier in this doc.

`POST /ac/v2/evc/charge/targetsoc/set`

Headers: common headers.

Request body (JSON):
```json
{
  "targetSOClist": [
    { "plugType": 0, "targetSOClevel": "<dcTargetPercent, int>" },
    { "plugType": 1, "targetSOClevel": "<acTargetPercent, int>" }
  ]
}
```
`plugType: 0` = DC (Level 3 / fast), `plugType: 1` = AC (Level 2 / slow).

No response body parsed.

**History**: an earlier AI-sourced answer for this feature (`POST /ac/v2/evc/vcursv`, a different body shape, and the *opposite* `plugType` assignment) was tested live and confirmed broken — HTTP 400 `"URL mapping Not Found"`, a gateway routing error meaning that path doesn't exist at all. This corrected path, body shape, and `plugType` direction instead match `HyundaiBlueLinkApiUSA.py`'s `set_charge_limits` exactly (the same reference source behind `StartClimateAsync`/`StopClimateAsync`, lock/unlock, and lights/horn — all separately confirmed live).

**Verified live** (2026-07-22): fired with `acTargetPercent: 55, dcTargetPercent: 90`. Returned HTTP 200 with a real transaction id, and `WaitForCommandAsync` resolved `SUCCESS` in well under a second — much faster than lock/unlock's ~17-28 seconds, consistent with this being an account-side config change rather than something requiring a round-trip to the vehicle's modem.

Checked against the official app afterward: DC showed exactly `90` (what was sent), AC showed `60` — a **rounding of the `55` sent, up to the nearest 10%**. This both confirms the `plugType` direction (`0 = DC`, `1 = AC`, exactly as implemented) and reveals a real constraint: **AC charge limits only accept 10% increments** and silently round rather than rejecting an in-between value. Whether DC has the same restriction wasn't distinguishable from this test, since `90` sent for DC was already a multiple of 10 — pass a non-multiple like `85` for DC to confirm one way or the other.

Confirmed live twice now, with a consistent structure both times (a failed login, and a rate-limited remote command) — this library does not currently parse this shape (failures just surface `Response.Failure(statusCode, rawBody)`), but it's worth documenting for anyone handling errors themselves:

```jsonc
{
  "errorSubCode": "<string, e.g. \"IDM_401_1\" or \"HT_533\">",
  "systemName": "<string, e.g. \"IDM\" or \"HATA\">",
  "functionName": "<string, e.g. \"customerLogin\" or \"remoteHornAndLight\">",
  "errorSubMessage": "<string, more detail>",
  "errorMessage": "<string, human-readable>",
  "errorCode": "<int, matches the HTTP status code>",
  "serviceName": "<string, e.g. \"AuthByPassword\" or \"RemoteHornAndLight\">"
}
```

Notably, **HTTP 502 is overloaded as a generic application-error status**, not just a real gateway failure — both an incorrect-password response and a "previous command still pending" rate-limit response came back as 502 with this same JSON shape distinguishing the actual cause via `errorSubCode`/`errorMessage`. Don't treat a 502 from this API as necessarily a transient infrastructure issue worth blindly retrying.

Confirmed real-world trigger for the rate-limit variant: firing two remote commands back-to-back on the same vehicle. The second one gets rejected with `errorSubCode: "HT_533"` until the first resolves.

---

## Command completion polling (`rmt/getRunningStatus`)

Every remote command (`StartClimateAsync`, `StopClimateAsync`, lock/unlock, lights/horn, charge start/stop) returns its transaction id in a response header — checked in this order: `tmsTid`, `transactionId`, `Xid`. HTTP 200 only means the command was *accepted*, not that it completed — actually confirming that requires polling this endpoint.

### `Response.TransactionId` / `Vehicle.WaitForCommandAsync(Response, ...)`

This is now wired up (as of 2026-07-22), as an **opt-in** step rather than automatic blocking — every command method still returns as fast as it did before; call `WaitForCommandAsync` yourself when you actually want to confirm the vehicle did something, not just that the server accepted the request.

- `Response` now carries a public `TransactionId` (populated automatically by `ResponseFactory` whenever a command response includes one — `null` for non-command responses like `StatusAsync`/`GetVehiclesAsync`) and an internal `ServiceType` the library uses to know which `service_type` value this particular command needs when polled.
- `Vehicle.WaitForCommandAsync(Response commandResponse, TimeSpan? pollInterval = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)`:
  - No-ops (`Response.Success()`) immediately if `commandResponse.TransactionId` is `null` — nothing to poll.
  - Otherwise polls `rmt/getRunningStatus` with the correct `service_type` for you, using the server-hinted 5-second `nextPollingInterval` once available (falling back to `pollInterval`, which itself defaults to 5 seconds), until `SUCCESS` (returns `Response.Success`), `ERROR` (returns `Response.Failure`), or `timeout` elapses (default 60 seconds; returns `Response.Failure(408, ...)`).

Usage:
```csharp
var lockResult = await vehicle.LockAsync();
var confirmed = await vehicle.WaitForCommandAsync(lockResult);
// confirmed.IsSuccessful is only true once the vehicle actually reported success, not just HTTP 200.
```

**A real-world caveat worth knowing before testing this yourself**: firing any of these commands triggers the same push notification to your phone that the official app would send, since it's the same backend regardless of which client calls it. This happened during this library's own live testing. Polling `WaitForCommandAsync` doesn't add extra notifications on top of that — but the underlying command itself will notify you (or whoever else has the app installed on the account) every time, library or not.

### Raw HTTP contract

**Verified live end-to-end** (2026-07-21), using `LockAsync`: fired a real lock, got `tmsTid` back, then polled the endpoint below once a second. Status genuinely progressed `PENDING` (16 times) -> `SUCCESS` over ~17 seconds — real latency to the vehicle, not an instantly-resolved placeholder.

`GET /ac/v2/rmt/getRunningStatus`

Headers: common headers, plus:
```
tid: <transaction id from the command's response header>
login_id: <BlueLinkClientConfig.Username>
service_type: REMOTE_POLL | LIGHTS_ONLY | HORN_AND_LIGHTS
```
`service_type` mapping (confirmed via source + the live lock test): `REMOTE_POLL` is the default, used for lock/unlock/climate start-stop/charge start-stop; `LIGHTS_ONLY` and `HORN_AND_LIGHTS` are used only for their respective commands.

Response body while pending:
```json
{ "status": "PENDING" }
```
Response body once resolved:
```json
{ "nextPollingInterval": "5", "tid": "<same transaction id>", "status": "SUCCESS" }
```
`status` is `"SUCCESS"`, `"ERROR"`, or presumably still-pending otherwise. `nextPollingInterval` (seen only in the resolved response, value `"5"`) reads as the server hinting a recommended polling cadence in seconds — once-a-second polling worked in this test but is likely more aggressive than intended; a well-behaved client should probably back off toward whatever this suggests, especially given the observed per-vehicle rate limiting on submitting a *new* command while one is still in flight.

---

## Known gaps

- EV plug status (`PluggedTo`) and estimated charge durations are now modeled (as of 2026-07-21). Charge limits are now settable via `SetChargeLimitsAsync` (as of 2026-07-22) — fully confirmed live, including the `plugType` direction (checked against the official app's UI). Discovered along the way: AC limits appear to only accept 10% increments and silently round rather than reject (untested whether DC has the same restriction). The full charge-schedule/off-peak-power configuration under `evStatus.reservChargeInfos` is confirmed present live but still not modeled.
- Also confirmed present live but unmodeled: a redundant GPS location on the status endpoint itself (`vehicleStatus.vehicleLocation.coord`), real per-tire PSI values, window-open state, and live seat-heater/vent state (as opposed to just the settable version this library already has).
- ~~Remote commands are treated as complete once the HTTP call returns 200~~ — resolved 2026-07-22: `Response.TransactionId` + `Vehicle.WaitForCommandAsync` now let callers opt in to real completion confirmation via `rmt/getRunningStatus`. Command methods themselves still return immediately (fast by default); nothing changed there.
