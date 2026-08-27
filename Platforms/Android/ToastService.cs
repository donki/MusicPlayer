using Android.Widget;
using MusicPlayer.Services;

namespace MusicPlayer.Platforms.Android;

/// <inheritdoc cref="IToastService"/>
public sealed class ToastService : IToastService
{
    public void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var context = global::Android.App.Application.Context;
            Toast.MakeText(context, message, ToastLength.Short)?.Show();
        });
    }
}
