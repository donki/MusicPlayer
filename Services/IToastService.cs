namespace MusicPlayer.Services;

/// <summary>
/// Aviso breve que no interrumpe. Para lo que si exige una decision se usa
/// <c>SocShared.ModernDialog</c>, nunca los dialogos del sistema (constitucion E.1).
/// </summary>
public interface IToastService
{
    void Show(string message);
}
