using System.Text.Json;
using BlooLynx.Models;
using Microsoft.Maui.Storage;

namespace BlooLynx.Maui.Android.Services;

/// <summary>
/// Loads persisted BlueLink configuration/session from secure storage, builds the <see cref="Client"/>, and tracks
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

    public BlueLinkClientConfig? Config { get; private set; }

    public Client? Client { get; private set; }

    public IReadOnlyList<Vehicle> Vehicles { get; private set; } = [];

    public bool IsInitialized { get; private set; }

    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// Loads config from secure storage and, if present, validates the stored session against the API. Safe to call
    /// once at app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        Config = await LoadAsync<BlueLinkClientConfig>(ConfigStorageKey);

        if (Config?.Username is null || Config.Password is null)
        {
            IsInitialized = true;
            StateChanged?.Invoke();
            return;
        }

        Client = new Client(Config, httpClientFactory, LoadSessionAsync, SaveSessionAsync);

        Response<IReadOnlyList<Vehicle>> result;
        try
        {
            result = await Client.GetVehiclesAsync();
        }
        catch (HttpRequestException)
        {
            result = Response<IReadOnlyList<Vehicle>>.Failure(0, "Could not reach the BlueLink API.");
        }

        IsInitialized = true;
        IsAuthenticated = result.IsSuccessful;

        if (result.IsSuccessful)
        {
            Vehicles = result.Data!;
        }

        StateChanged?.Invoke();
    }

    public Task SaveConfigAsync(BlueLinkClientConfig config)
    {
        Config = config;
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

        Client = new Client(config, httpClientFactory, LoadSessionAsync, SaveSessionAsync);

        Response result;
        try
        {
            result = await Client.LoginAsync();
        }
        catch (HttpRequestException)
        {
            result = Response.Failure(0, "Could not reach the BlueLink API.");
        }

        if (result.IsSuccessful)
        {
            IsAuthenticated = true;
            StateChanged?.Invoke();
        }

        return result;
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
