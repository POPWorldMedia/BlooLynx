using System.Text.Json;
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
/// </remarks>
public class StateService(IHttpClientFactory httpClientFactory)
{
    private const string ConfigStorageKey = "bloolynx_config";
    private const string SessionStorageKey = "bloolynx_session";

    public event Action? StateChanged;

    /// <summary>Raised on every <see cref="ResumeAsync"/> call while authenticated. Pages showing per-vehicle
    /// data (e.g. status) should react to this to refresh what they're currently displaying.</summary>
    public event Action? Resumed;

    private BlueLinkClientConfig? _config;
    private Client? _client;

    public IReadOnlyList<Vehicle> Vehicles { get; private set; } = [];

    public bool IsInitialized { get; private set; }

    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// Loads config from secure storage and, if present, validates the stored session against the API. Safe to call
    /// once at app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        _config = await LoadAsync<BlueLinkClientConfig>(ConfigStorageKey);

        if (_config?.Username is null || _config.Password is null)
        {
            IsInitialized = true;
            StateChanged?.Invoke();
            return;
        }

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

        _config = null;
        _client = null;
        Vehicles = [];
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
