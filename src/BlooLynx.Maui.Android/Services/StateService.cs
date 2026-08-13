using System.Text.Json;
using BlooLynx.Maui.Android.Models;
using BlooLynx.Models;

namespace BlooLynx.Maui.Android.Services;

/// <summary>
/// Loads persisted BlueLink configuration/session from secure storage, builds the <see cref="_client"/>, and tracks
/// whether the app should show the home page or the login page.
/// </summary>
/// <remarks>
/// Deliberately avoids <see cref="Microsoft.AspNetCore.Components.NavigationManager"/> for the login/home switch:
/// in MAUI Blazor Hybrid, NavigationManager isn't usable until the WebView reports its first navigation has
/// completed, which happens after the component tree's first render — so calling it during startup throws. Routes
/// reacts to <see cref="StateChanged"/> instead and swaps components directly.
///
/// Three independent things live in <see cref="SecureStorage"/>, each under its own key, and none of them overlap:
/// <list type="bullet">
/// <item><description><see cref="ConfigStorageKey"/>: the BlueLink username/password, as a serialized
/// <see cref="BlueLinkClientConfig"/>. Loaded at startup by <see cref="InitializeAsync"/> to auto-login without
/// re-prompting; written by <see cref="SaveConfigAsync"/>.</description></item>
/// <item><description><see cref="SessionStorageKey"/>: the OAuth <see cref="Session"/> (access/refresh token),
/// managed entirely by <see cref="Client"/> itself via the <see cref="LoadSessionAsync"/>/<see cref="SaveSessionAsync"/>
/// callbacks — this class never reads or writes it directly.</description></item>
/// <item><description><see cref="PinStorageKey"/>: the confirmed-working remote-command PIN, as a plain string.
/// Deliberately separate from <see cref="ConfigStorageKey"/> (not bundled into <see cref="BlueLinkClientConfig"/>'s
/// storage) so it's unambiguous where it lives and how it's cleared. See <see cref="SetPendingPin"/>/
/// <see cref="ConfirmPinAsync"/>/<see cref="ForgetPinAsync"/>.</description></item>
/// <item><description><see cref="VehicleSettingsStorageKeyPrefix"/>: per-vehicle, user-supplied
/// <see cref="VehicleSettings"/> (battery capacity, efficiency), one entry per VIN. Not cleared by
/// <see cref="LogoutAsync"/> — these describe the physical vehicle, not the account session, and logging back
/// in with the same vehicle should find them still in place. See <see cref="GetVehicleSettingsAsync"/>/
/// <see cref="SaveVehicleSettingsAsync"/>.</description></item>
/// <item><description><see cref="ShowEstimatedRangeStorageKey"/>: whether the home/charging range display is
/// currently toggled to the estimated figure rather than the vehicle-reported one — a display preference, not
/// tied to any one vehicle, so it isn't cleared by <see cref="LogoutAsync"/> either. Loaded eagerly into
/// <see cref="ShowEstimatedRange"/> by <see cref="InitializeAsync"/> rather than lazily like the others — see
/// that property's remarks. Written via <see cref="SetShowEstimatedRangeAsync"/>.</description></item>
/// </list>
/// <see cref="LogoutAsync"/> removes the first three keys explicitly.
/// </remarks>
public class StateService(IHttpClientFactory httpClientFactory)
{
    private const string ConfigStorageKey = "bloolynx_config";
    private const string SessionStorageKey = "bloolynx_session";
    private const string PinStorageKey = "bloolynx_pin";
    private const string VehicleSettingsStorageKeyPrefix = "bloolynx_vehicle_settings_";
    private const string ShowEstimatedRangeStorageKey = "bloolynx_show_estimated_range";

    public event Action? StateChanged;

    /// <summary>Raised on every <see cref="ResumeAsync"/> call while authenticated. Pages showing per-vehicle
    /// data (e.g. status) should react to this to refresh what they're currently displaying.</summary>
    public event Action? Resumed;

    private BlueLinkClientConfig? _config;
    private Client? _client;

    public IReadOnlyList<Vehicle> Vehicles { get; private set; } = [];

    public bool IsInitialized { get; private set; }

    public bool IsAuthenticated { get; private set; }

    public bool HasPin => !string.IsNullOrEmpty(_config?.Pin);

    /// <summary>The vehicle currently selected on the home page, if any. Shared here (rather than kept as
    /// component-local state) so a page navigated to from Home — e.g. the climate page — knows which vehicle
    /// it's acting on without needing it passed through a route parameter.</summary>
    public Vehicle? SelectedVehicle { get; set; }

    /// <summary>The last status fetched for <see cref="SelectedVehicle"/>. <see cref="Status"/> and its nested
    /// types are mutable classes, so pages that hold this same reference can update it in place after a
    /// command succeeds (e.g. flipping <c>Climate.Active</c> after starting climate) without forcing a full
    /// network re-fetch — as long as they mutate the cached instance rather than replacing it.</summary>
    public Status? CachedStatus { get; set; }

    /// <summary>Whether the range display should default to the estimated figure rather than the vehicle-reported
    /// one. Loaded up front by <see cref="InitializeAsync"/> — before <c>Routes</c> renders anything past its own
    /// loading spinner — specifically so <c>VehicleControls</c> can read the saved preference on its very first
    /// render instead of starting from <c>false</c> and flipping a moment later once an async load resolves.</summary>
    public bool ShowEstimatedRange { get; private set; }

    /// <summary>
    /// Loads config from secure storage and, if present, validates the stored session against the API. Safe to call
    /// once at app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        ShowEstimatedRange = await LoadAsync<bool?>(ShowEstimatedRangeStorageKey) ?? false;
        _config = await LoadAsync<BlueLinkClientConfig>(ConfigStorageKey);

        if (_config?.Username is null || _config.Password is null)
        {
            IsInitialized = true;
            StateChanged?.Invoke();
            return;
        }

        _config.Pin = await LoadAsync<string>(PinStorageKey);
        _client = new Client(_config, httpClientFactory, LoadSessionAsync, SaveSessionAsync);

        var vehiclesResult = await FetchVehiclesAsync();

        IsInitialized = true;
        IsAuthenticated = vehiclesResult.IsSuccessful;

        if (vehiclesResult.IsSuccessful)
        {
            Vehicles = vehiclesResult.Data!;
        }

        StateChanged?.Invoke();
    }

    /// <summary>Call when the app is resumed from the background. Re-fetches the vehicle list and raises
    /// <see cref="Resumed"/> so pages showing per-vehicle data can refresh what they're currently displaying.</summary>
    public async Task ResumeAsync()
    {
        if (!IsAuthenticated || _client is null)
        {
            return;
        }

        Resumed?.Invoke();

        var vehiclesResult = await FetchVehiclesAsync();
        if (vehiclesResult.IsSuccessful)
        {
            Vehicles = vehiclesResult.Data!;
            StateChanged?.Invoke();
        }
    }

    private Task SaveConfigAsync(BlueLinkClientConfig config)
    {
        _config = config;
        return SaveAsync(ConfigStorageKey, config);
    }

    /// <summary>
    /// Saves the given credentials and logs in against the API. Returns the login response so the caller can show
    /// an error message on failure.
    /// </summary>
    public async Task<Response> LoginAsync(string username, string password)
    {
        var config = new BlueLinkClientConfig { Username = username, Password = password };
        await SaveConfigAsync(config);

        config.Pin = await LoadAsync<string>(PinStorageKey);
        _client = new Client(config, httpClientFactory, LoadSessionAsync, SaveSessionAsync);

        Response result;
        try
        {
            result = await _client.LoginAsync();
        }
        catch (HttpRequestException)
        {
            result = Response.Failure(0, "Could not reach the BlueLink API.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            result = Response.Failure(0, "Could not reach the BlueLink API.");
        }

        if (result.IsSuccessful)
        {
            var vehiclesResult = await FetchVehiclesAsync();
            if (vehiclesResult.IsSuccessful)
            {
                Vehicles = vehiclesResult.Data!;
            }

            IsAuthenticated = true;
            StateChanged?.Invoke();
        }

        return result;
    }

    /// <summary>Sets the PIN in memory only, so the next command sent to the API uses it, without persisting it —
    /// call <see cref="ConfirmPinAsync"/> once a command using it has actually succeeded against the API.</summary>
    public void SetPendingPin(string pin)
    {
        if (_config is not null)
        {
            _config.Pin = pin;
        }
    }

    /// <summary>Forgets a PIN that turned out to be wrong — whether it was only just set via <see cref="SetPendingPin"/>
    /// and never confirmed, or was confirmed and saved in an earlier session and has since been found bad. Clears
    /// both the in-memory value and <see cref="PinStorageKey"/>, so the user is prompted for a fresh one next time
    /// instead of the bad one being silently reused.</summary>
    public Task ForgetPinAsync()
    {
        if (_config is not null)
        {
            _config.Pin = null;
        }

        SecureStorage.Default.Remove(PinStorageKey);
        return Task.CompletedTask;
    }

    /// <summary>Persists the currently in-memory PIN (set via <see cref="SetPendingPin"/>) to <see cref="PinStorageKey"/>
    /// so the user is never asked for it again. Does not touch <see cref="ConfigStorageKey"/> — the PIN is stored
    /// independently of the username/password.</summary>
    public Task ConfirmPinAsync() => _config?.Pin is null ? Task.CompletedTask : SaveAsync(PinStorageKey, _config.Pin);

    /// <summary>Loads the user-supplied battery capacity/efficiency for <paramref name="vehicle"/>, or an empty
    /// <see cref="VehicleSettings"/> if none has been saved yet.</summary>
    public async Task<VehicleSettings> GetVehicleSettingsAsync(Vehicle vehicle) =>
        await LoadAsync<VehicleSettings>(VehicleSettingsStorageKeyPrefix + vehicle.VehicleConfig.Vin) ?? new VehicleSettings();

    /// <summary>Persists the user-supplied battery capacity/efficiency for <paramref name="vehicle"/>.</summary>
    public Task SaveVehicleSettingsAsync(Vehicle vehicle, VehicleSettings settings) =>
        SaveAsync(VehicleSettingsStorageKeyPrefix + vehicle.VehicleConfig.Vin, settings);

    /// <summary>Updates <see cref="ShowEstimatedRange"/> in memory (so every open page reflects the change
    /// immediately) and persists it for next time.</summary>
    public Task SetShowEstimatedRangeAsync(bool showEstimated)
    {
        ShowEstimatedRange = showEstimated;
        return SaveAsync(ShowEstimatedRangeStorageKey, showEstimated);
    }

    /// <summary>Locks or unlocks <paramref name="vehicle"/>, waiting for the command to actually complete on the
    /// vehicle (via <see cref="Vehicle.WaitForCommandAsync"/>) rather than trusting the initial HTTP 200.</summary>
    public async Task<Response> ToggleVehicleLockAsync(Vehicle vehicle, bool currentlyLocked)
    {
        try
        {
            var commandResult = currentlyLocked ? await vehicle.UnlockAsync() : await vehicle.LockAsync();
            return commandResult.IsSuccessful ? await vehicle.WaitForCommandAsync(commandResult) : commandResult;
        }
        catch (HttpRequestException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
    }

    /// <summary>Starts or stops charging <paramref name="vehicle"/>, waiting for the command to actually complete on
    /// the vehicle (via <see cref="Vehicle.WaitForCommandAsync"/>) rather than trusting the initial HTTP 200.</summary>
    public async Task<Response> ToggleVehicleChargeAsync(Vehicle vehicle, bool currentlyCharging)
    {
        try
        {
            var commandResult = currentlyCharging ? await vehicle.StopChargeAsync() : await vehicle.StartChargeAsync();
            return commandResult.IsSuccessful ? await vehicle.WaitForCommandAsync(commandResult) : commandResult;
        }
        catch (HttpRequestException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
    }

    /// <summary>Starts climate on <paramref name="vehicle"/> with the given <paramref name="options"/>, waiting
    /// for the command to actually complete on the vehicle (via <see cref="Vehicle.WaitForCommandAsync"/>)
    /// rather than trusting the initial HTTP 200.</summary>
    public async Task<Response> StartVehicleClimateAsync(Vehicle vehicle, StartOptions options)
    {
        try
        {
            var commandResult = await vehicle.StartClimateAsync(options);
            return commandResult.IsSuccessful ? await vehicle.WaitForCommandAsync(commandResult) : commandResult;
        }
        catch (HttpRequestException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
    }

    /// <summary>Stops climate on <paramref name="vehicle"/>, waiting for the command to actually complete on the
    /// vehicle (via <see cref="Vehicle.WaitForCommandAsync"/>) rather than trusting the initial HTTP 200.</summary>
    public async Task<Response> StopVehicleClimateAsync(Vehicle vehicle)
    {
        try
        {
            var commandResult = await vehicle.StopClimateAsync();
            return commandResult.IsSuccessful ? await vehicle.WaitForCommandAsync(commandResult) : commandResult;
        }
        catch (HttpRequestException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
    }

    /// <summary>Sets AC/DC charge target percentages on <paramref name="vehicle"/>, waiting for the command to
    /// actually complete on the vehicle (via <see cref="Vehicle.WaitForCommandAsync"/>) rather than trusting the
    /// initial HTTP 200.</summary>
    public async Task<Response> SetChargeLimitsAsync(Vehicle vehicle, int acTargetPercent, int dcTargetPercent)
    {
        try
        {
            var commandResult = await vehicle.SetChargeLimitsAsync(acTargetPercent, dcTargetPercent);
            return commandResult.IsSuccessful ? await vehicle.WaitForCommandAsync(commandResult) : commandResult;
        }
        catch (HttpRequestException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return Response.Failure(0, "Could not reach the BlueLink API.");
        }
    }

    private async Task<Response<IReadOnlyList<Vehicle>>> FetchVehiclesAsync()
    {
        try
        {
            return await _client!.GetVehiclesAsync();
        }
        catch (HttpRequestException)
        {
            return Response<IReadOnlyList<Vehicle>>.Failure(0, "Could not reach the BlueLink API.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return Response<IReadOnlyList<Vehicle>>.Failure(0, "Could not reach the BlueLink API.");
        }
    }

    /// <summary>Invalidates the session with the API (best-effort), then clears stored credentials so
    /// the app falls back to the login page.</summary>
    public async Task LogoutAsync()
    {
        if (_client is not null)
        {
            try
            {
                await _client.LogoutAsync();
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
            }
        }

        SecureStorage.Default.Remove(ConfigStorageKey);
        SecureStorage.Default.Remove(SessionStorageKey);
        SecureStorage.Default.Remove(PinStorageKey);

        _config = null;
        _client = null;
        Vehicles = [];
        SelectedVehicle = null;
        CachedStatus = null;
        IsAuthenticated = false;

        StateChanged?.Invoke();
    }

    private Task<Session?> LoadSessionAsync(CancellationToken cancellationToken) => LoadAsync<Session>(SessionStorageKey);

    private Task SaveSessionAsync(Session session, CancellationToken cancellationToken) => SaveAsync(SessionStorageKey, session);

    private static async Task<T?> LoadAsync<T>(string key)
    {
        var json = await SecureStorage.Default.GetAsync(key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    private static async Task SaveAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        await SecureStorage.Default.SetAsync(key, json);
    }
}
