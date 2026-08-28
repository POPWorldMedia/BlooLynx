namespace BlooLynx.Maui.Android;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	/// <summary>Bridges the Android hardware/gesture back button into Blazor's client-side router.
	/// <c>BlazorWebView</c>'s navigation lives entirely in the WebView's own JS history, separate from
	/// MAUI's native back-button handling, so without this override, pressing back on a sub-page like
	/// <c>/climate</c>, <c>/Charging</c>, <c>/Tires</c>, <c>/Other</c>, or <c>/Configure</c> would just
	/// exit/background the app instead of navigating within it.
	/// </summary>
	/// <remarks>
	/// Everywhere else (i.e. Home) explicitly quits rather than falling through to the base
	/// <c>OnBackButtonPressed</c>'s default handling: that default apparently doesn't
	/// reliably exit here — it falls through to the native WebView's own back-history handling, which (if
	/// anything is still sitting behind the current entry in its JS history) navigates within the WebView
	/// instead of exiting the app. Explicitly quitting sidesteps that entirely instead of depending on
	/// exactly what's left in that history stack.
	/// </remarks>
	protected override bool OnBackButtonPressed()
	{
		var webView = blazorWebView.Handler?.PlatformView as global::Android.Webkit.WebView;
		var currentPath = webView?.Url is { } url ? new Uri(url).AbsolutePath.TrimEnd('/') : null;

		if (currentPath is "/climate" or "/Charging" or "/Tires" or "/Other" or "/Configure")
		{
			webView!.EvaluateJavascript("Blazor.navigateTo('/')", null);
			return true;
		}

		Application.Current?.Quit();
		return true;
	}
}
