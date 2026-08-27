namespace MusicPlayer;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
#if DEBUG
        SocShared.AuthorNotes.Attach(window);   // notas de autor: SOLO Debug, desactivado en Release (anexo E.2)
#endif
        return window;
    }
}
