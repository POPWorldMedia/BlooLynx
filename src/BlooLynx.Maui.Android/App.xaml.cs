using BlooLynx.Maui.Android.Services;

namespace BlooLynx.Maui.Android;

public partial class App : Application
{
	private readonly StateService _stateService;

	public App(StateService stateService)
	{
		InitializeComponent();
		_stateService = stateService;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "BlooLynx.Maui.Android" };
		window.Resumed += OnWindowResumed;
		return window;
	}

	private async void OnWindowResumed(object? sender, EventArgs e)
	{
		await _stateService.ResumeAsync();
	}
}
