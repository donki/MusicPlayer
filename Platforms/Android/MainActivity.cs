using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using AndroidView = Android.Views.View;

namespace MusicPlayer;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>Codigo con el que vuelve la confirmacion de borrado que muestra el sistema.</summary>
    private const int DeleteRequestCode = 4711;

    private static TaskCompletionSource<bool>? _pendingDeleteConfirmation;

    /// <summary>
    /// Actividad viva, que hace falta para lanzar la confirmacion de borrado del sistema. Es
    /// <c>null</c> cuando el proceso lo ha arrancado Android Auto sin abrir la interfaz.
    /// </summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
        ApplySystemBarInsets();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
            Current = null;

        base.OnDestroy();
    }

    /// <summary>
    /// Lanza el dialogo de borrado del sistema y espera la decision del usuario. Devuelve
    /// <c>false</c> si el usuario lo rechaza; nunca borra nada por su cuenta.
    /// </summary>
    public Task<bool> ConfirmDeleteAsync(IntentSender sender)
    {
        _pendingDeleteConfirmation?.TrySetResult(false);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingDeleteConfirmation = completion;

        StartIntentSenderForResult(sender, DeleteRequestCode, null, 0, 0, 0);
        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != DeleteRequestCode)
            return;

        var completion = _pendingDeleteConfirmation;
        _pendingDeleteConfirmation = null;
        completion?.TrySetResult(resultCode == Result.Ok);
    }

    /// <summary>
    /// Desde Android 15 el sistema dibuja de borde a borde: se separa el contenido del reloj y de
    /// la barra inferior, y se pinta el hueco con el indigo de marca (constitucion E.3).
    /// </summary>
    private void ApplySystemBarInsets()
    {
        var content = FindViewById(global::Android.Resource.Id.Content);
        if (content is null)
            return;

        content.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#2A1CB8"));
        ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarInsetsListener());

        var controller = Window is not null ? WindowCompat.GetInsetsController(Window, Window.DecorView) : null;
        if (controller is not null)
        {
            controller.AppearanceLightStatusBars = false;
            controller.AppearanceLightNavigationBars = false;
        }
    }

    private sealed class SystemBarInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(AndroidView? view, WindowInsetsCompat? insets)
        {
            var consumed = WindowInsetsCompat.Consumed!;
            if (view is null || insets is null)
                return consumed;

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            if (bars is not null)
                view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);

            return consumed;
        }
    }
}
