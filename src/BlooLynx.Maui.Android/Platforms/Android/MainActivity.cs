using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace BlooLynx.Maui.Android;

/// <summary>
/// Targeting API 35+ means edge-to-edge is mandatory - <see cref="WindowCompat.SetDecorFitsSystemWindows"/>
/// is deprecated and does nothing once the app opts into (or is forced into) edge-to-edge, so the classic
/// "let Android reserve space for system bars" behavior no longer applies. Instead we read the actual
/// system bar and IME (keyboard) insets ourselves and pad the content view by them, which is Android's
/// documented replacement for that behavior. This also covers the keyboard: with edge-to-edge forced,
/// adjustResize no longer shrinks the window, so the IME inset has to be applied by hand too.
/// </summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var rootView = FindViewById(global::Android.Resource.Id.Content)!;
        ViewCompat.SetOnApplyWindowInsetsListener(rootView, new WindowInsetsListener());
    }

    private class WindowInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(global::Android.Views.View? view, WindowInsetsCompat? insets)
        {
            if (view is null || insets is null)
            {
                return insets;
            }

            var systemBars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars())!;
            var ime = insets.GetInsets(WindowInsetsCompat.Type.Ime())!;
            view.SetPadding(systemBars.Left, systemBars.Top, systemBars.Right, Math.Max(systemBars.Bottom, ime.Bottom));
            return insets;
        }
    }
}
