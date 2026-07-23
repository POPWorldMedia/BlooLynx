using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlooLynx.Models;

namespace BlooLynx;

public class Client(
    BlueLinkClientConfig userConfig,
    IHttpClientFactory httpClientFactory,
    Func<CancellationToken, Task<Session?>>? loadSession = null,
    Func<Session, CancellationToken, Task>? saveSession = null)
{
    public const string HttpClientName = "BlooLynx";

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(HttpClientName);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public BlueLinkClientConfig UserConfig { get; } = userConfig;

    private Session Session { get; } = new();

    /// <summary>Whether this client currently holds an access token — a local check, not a network call.</summary>
    public bool IsAuthenticated => Session.AccessToken is not null;

    private const string Host = "api.telematics.hyundaiusa.com";
    private const string BaseUrl = "https://api.telematics.hyundaiusa.com";
    private const string ClientId = "m66129Bb-em93-SPAHYN-bZ91-am4540zp19920";
    private const string ClientSecret = "v558o935-6nne-423i-baa8";

    private const int TokenRefreshBufferSeconds = 60;

    private volatile bool _sessionLoadAttempted;

    /// <summary>
    /// Loads a previously saved session via the <c>loadSession</c> delegate, if one was registered and this is the
    /// first time it's needed. Runs at most once per client instance (even if it throws or finds nothing), and is
    /// folded into the same lock <see cref="RefreshAccessTokenAsync"/> uses so a fresh client transparently picks up
    /// a persisted session on its first call without callers having to do anything extra.
    /// </summary>
    private async Task EnsureSessionLoadedAsync(CancellationToken cancellationToken)
    {
        if (_sessionLoadAttempted || loadSession is null)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionLoadAttempted)
            {
                return;
            }

            var restored = await loadSession(cancellationToken).ConfigureAwait(false);
            if (restored is not null)
            {
                Session.AccessToken = restored.AccessToken;
                Session.RefreshToken = restored.RefreshToken;
                Session.TokenExpiresAt = restored.TokenExpiresAt;
            }
        }
        finally
        {
            _sessionLoadAttempted = true;
            _refreshLock.Release();
        }
    }

    private Task PersistSessionAsync(CancellationToken cancellationToken) =>
        saveSession?.Invoke(Session, cancellationToken) ?? Task.CompletedTask;

    private async Task RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSessionLoadedAsync(cancellationToken).ConfigureAwait(false);

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secondsUntilExpiry = Session.TokenExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var shouldRefresh = secondsUntilExpiry <= TokenRefreshBufferSeconds;

            if (string.IsNullOrEmpty(Session.RefreshToken) || !shouldRefresh)
            {
                return;
            }

            var result = await RequestTokenAsync(
                "v2/ac/oauth/token/refresh", new { refresh_token = Session.RefreshToken }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccessful)
            {
                // The refresh token is presumed dead (expired/revoked): clear the session so we stop retrying it on
                // every subsequent call, and require an explicit LoginAsync instead.
                Session.AccessToken = null;
                Session.RefreshToken = null;
                Session.TokenExpiresAt = 0;
                await PersistSessionAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            ApplyToken(result.Data!);
            await PersistSessionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<Response> LoginAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequestTokenAsync(
            "v2/ac/oauth/token", new { username = UserConfig.Username, password = UserConfig.Password }, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccessful)
        {
            return Response.Failure(result.ResponseCode, result.ErrorMessage!);
        }

        ApplyToken(result.Data!);
        await PersistSessionAsync(cancellationToken).ConfigureAwait(false);
        return Response.Success(result.ResponseCode);
    }

    public async Task<Response> LogoutAsync(CancellationToken cancellationToken = default)
    {
        Session.AccessToken = null;
        Session.RefreshToken = null;
        Session.TokenExpiresAt = 0;
        await PersistSessionAsync(cancellationToken).ConfigureAwait(false);
        return Response.Success();
    }

    /// <summary>Posts a credentials/refresh payload to an OAuth token endpoint.</summary>
    private async Task<Response<TokenResponse>> RequestTokenAsync(string service, object payload, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string?>
        {
            ["User-Agent"] = "PostmanRuntime/7.26.10",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
        };

        using var response = await RawSendAsync(HttpMethod.Post, service, headers, JsonContent.Create(payload), cancellationToken)
            .ConfigureAwait(false);

        return await ResponseFactory.FromHttpResponseAsync(response, body => JsonSerializer.Deserialize<TokenResponse>(body)!)
            .ConfigureAwait(false);
    }

    private void ApplyToken(TokenResponse token)
    {
        Session.AccessToken = token.AccessToken;
        Session.RefreshToken = token.RefreshToken;
        Session.TokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + token.ExpiresIn;
    }

    private static readonly TimeZoneInfo EasternTimeZone = GetEasternTimeZone();

    private static TimeZoneInfo GetEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    /// <summary>Hours offset from UTC for US Eastern time, accounting for daylight saving.</summary>
    private static string EasternUtcOffsetHeader() =>
        EasternTimeZone.GetUtcOffset(DateTimeOffset.UtcNow).Hours.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the common header set used by every per-vehicle endpoint.
    /// </summary>
    /// <param name="vehicleConfig">The vehicle whose identifiers (VIN, registration ID, generation, brand) go into the headers.</param>
    /// <param name="refresh">
    /// Controls the REFRESH header, which only the vehicleStatus endpoint honors (forces a live poll of the
    /// vehicle's modem instead of a cached reading). This is nullable rather than a plain bool on purpose: the
    /// original (undocumented, reverse-engineered) API call always sent REFRESH explicitly as "true"/"false" for
    /// status requests, and never sent it at all for every other endpoint. We don't know whether the server treats
    /// "header absent" the same as "header present with false" for an undocumented API, so null preserves "omit
    /// entirely" as a distinct case from "explicitly false" rather than collapsing them for the sake of a tidier
    /// signature.
    /// </param>
    internal Dictionary<string, string?> BuildHeaders(VehicleConfig vehicleConfig, bool? refresh = null)
    {
        var headers = new Dictionary<string, string?>
        {
            ["access_token"] = Session.AccessToken,
            ["client_id"] = ClientId,
            ["Host"] = Host,
            ["User-Agent"] = "okhttp/3.12.0",
            ["registrationId"] = vehicleConfig.RegistrationId,
            ["gen"] = vehicleConfig.Generation,
            ["username"] = UserConfig.Username,
            ["vin"] = vehicleConfig.Vin,
            ["APPCLOUD-VIN"] = vehicleConfig.Vin,
            ["Language"] = "0",
            ["to"] = "ISS",
            ["encryptFlag"] = "false",
            ["from"] = "SPA",
            ["brandIndicator"] = vehicleConfig.BrandIndicator,
            ["bluelinkservicepin"] = UserConfig.Pin,
            ["offset"] = EasternUtcOffsetHeader(),
        };

        if (refresh is not null)
        {
            headers["REFRESH"] = refresh.Value.ToString().ToLowerInvariant();
        }

        return headers;
    }

    /// <summary>Sends an authenticated request, refreshing the access token first if it is due to expire.</summary>
    internal async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, Dictionary<string, string?> headers, HttpContent? content, CancellationToken cancellationToken = default)
    {
        await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        headers["access_token"] = Session.AccessToken;

        return await RawSendAsync(method, path, headers, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds and sends a single HTTP request. Does not apply auth headers or refresh the token.</summary>
    private async Task<HttpResponseMessage> RawSendAsync(
        HttpMethod method, string path, Dictionary<string, string?> headers, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{BaseUrl}/{path.TrimStart('/')}");
        request.Content = content;
        foreach (var (key, value) in headers)
        {
            if (value is not null)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Response<IReadOnlyList<Vehicle>>> GetVehiclesAsync(CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["Host"] = Host,
            ["User-Agent"] = "okhttp/3.12.0",
            ["payloadGenerated"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture),
            ["includeNonConnectedVehicles"] = "Y",
        };

        using var response = await SendAsync(
            HttpMethod.Get, $"ac/v2/enrollment/details/{UserConfig.Username}", headers, null, cancellationToken).ConfigureAwait(false);

        return await ResponseFactory.FromHttpResponseAsync(response, ParseVehicles).ConfigureAwait(false);
    }

    private IReadOnlyList<Vehicle> ParseVehicles(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("enrolledVehicleDetails", out var enrolled) || enrolled.ValueKind != JsonValueKind.Array)
        {
            return new List<Vehicle>();
        }

        var vehicles = new List<Vehicle>();
        foreach (var item in enrolled.EnumerateArray())
        {
            var details = item.GetProperty("vehicleDetails");
            var config = new VehicleConfig
            {
                Nickname = details.GetProperty("nickName").GetString() ?? string.Empty,
                Name = details.GetProperty("nickName").GetString() ?? string.Empty,
                Vin = details.GetProperty("vin").GetString() ?? string.Empty,
                RegistrationDate = details.TryGetProperty("enrollmentDate", out var regDate) ? regDate.GetString() ?? string.Empty : string.Empty,
                BrandIndicator = details.TryGetProperty("brandIndicator", out var bi) ? bi.GetString() ?? string.Empty : string.Empty,
                RegistrationId = details.TryGetProperty("regid", out var regId) ? regId.GetString() ?? string.Empty : string.Empty,
                Generation = details.TryGetProperty("vehicleGeneration", out var gen) ? gen.GetString() ?? string.Empty : string.Empty,
                ModelYear = details.TryGetProperty("modelYear", out var modelYear) ? modelYear.GetString() ?? string.Empty : string.Empty,
                ModelCode = details.TryGetProperty("modelCode", out var modelCode) ? modelCode.GetString() ?? string.Empty : string.Empty,
                Trim = details.TryGetProperty("trim", out var trim) ? trim.GetString() ?? string.Empty : string.Empty,
                IsEV = details.TryGetProperty("evStatus", out var evStatus) && evStatus.GetString() == "E",
            };

            vehicles.Add(new Vehicle(config, this));
        }

        return vehicles;
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long ExpiresIn { get; set; }
    }
}