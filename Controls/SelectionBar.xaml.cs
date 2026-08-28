namespace MusicPlayer.Controls;

/// <summary>
/// Barra contextual del modo de seleccion multiple: cuantas canciones hay marcadas y que se puede
/// hacer con ellas. Solo emite eventos; quien decide que significa cada accion es
/// <see cref="Helpers.SongSelection"/>, que es el que conoce la lista.
/// </summary>
public partial class SelectionBar : ContentView
{
    public SelectionBar() => InitializeComponent();

    public event EventHandler? CloseClicked;

    public event EventHandler? SelectAllClicked;

    public event EventHandler? PlayClicked;

    public event EventHandler? AddToPlaylistClicked;

    public event EventHandler? MoreClicked;

    /// <summary>
    /// Refresca la barra. Sin nada marcado las acciones se apagan en vez de desaparecer: asi la
    /// barra no cambia de tamano al ir marcando y desmarcando.
    /// </summary>
    public void Update(string countText, bool hasSelection)
    {
        CountLabel.Text = countText;
        PlayButton.IsEnabled = hasSelection;
        AddToPlaylistButton.IsEnabled = hasSelection;
        MoreButton.IsEnabled = hasSelection;
    }

    private void OnCloseClicked(object? sender, EventArgs e) => CloseClicked?.Invoke(this, EventArgs.Empty);

    private void OnSelectAllClicked(object? sender, EventArgs e) => SelectAllClicked?.Invoke(this, EventArgs.Empty);

    private void OnPlayClicked(object? sender, EventArgs e) => PlayClicked?.Invoke(this, EventArgs.Empty);

    private void OnAddToPlaylistClicked(object? sender, EventArgs e) => AddToPlaylistClicked?.Invoke(this, EventArgs.Empty);

    private void OnMoreClicked(object? sender, EventArgs e) => MoreClicked?.Invoke(this, EventArgs.Empty);
}
