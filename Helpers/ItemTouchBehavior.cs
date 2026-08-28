using AndroidView = Android.Views.View;

namespace MusicPlayer.Helpers;

/// <summary>
/// Toque y pulsacion larga sobre una fila de la lista, resueltos con los gestos nativos de Android.
/// </summary>
/// <remarks>
/// MAUI no trae gesto de pulsacion larga, y las dos alternativas de MAUI fallan en Android
/// (comprobado en dispositivo, no deducido):
/// <list type="bullet">
///   <item><description><c>PointerGestureRecognizer</c> no dispara <c>PointerPressed</c> con el
///   dedo: mantener pulsado se comporta como un toque normal.</description></item>
///   <item><description>Enganchar <c>View.LongClick</c> dejando el <c>TapGestureRecognizer</c> en
///   la fila tampoco funciona: el detector de gestos de MAUI consume el evento tactil antes de que
///   <c>onTouchEvent</c> llegue a evaluar la pulsacion larga.</description></item>
/// </list>
/// Por eso este comportamiento se encarga de <b>los dos</b> gestos: al no quedar ningun
/// <c>GestureRecognizer</c> en la fila, MAUI no instala su detector y Android reparte
/// <c>Click</c> y <c>LongClick</c> con su tiempo, su vibracion y su cancelacion al desplazar
/// la lista.
///
/// Los eventos se emiten con la <b>fila</b> como emisor, no con el comportamiento: MAUI no
/// propaga el <c>BindingContext</c> a los comportamientos, asi que el manejador se quedaba sin
/// saber que elemento se habia pulsado.
/// </remarks>
public sealed class ItemTouchBehavior : PlatformBehavior<View, AndroidView>
{
    private View? _row;

    /// <summary>Toque corto sobre la fila.</summary>
    public event EventHandler? Tapped;

    /// <summary>Pulsacion mantenida sobre la fila.</summary>
    public event EventHandler? LongPressed;

    protected override void OnAttachedTo(View bindable, AndroidView platformView)
    {
        _row = bindable;
        platformView.Clickable = true;
        platformView.LongClickable = true;
        platformView.Click += OnClick;
        platformView.LongClick += OnLongClick;
    }

    protected override void OnDetachedFrom(View bindable, AndroidView platformView)
    {
        platformView.Click -= OnClick;
        platformView.LongClick -= OnLongClick;
        _row = null;
    }

    private void OnClick(object? sender, EventArgs e) => Tapped?.Invoke(_row, EventArgs.Empty);

    private void OnLongClick(object? sender, AndroidView.LongClickEventArgs e)
    {
        // Consumir el evento evita que al soltar llegue tambien el toque, que reproduciria la cancion.
        e.Handled = true;
        LongPressed?.Invoke(_row, EventArgs.Empty);
    }
}
