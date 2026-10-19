using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace CifraToken.App;

sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default; // segue o tema claro/escuro do sistema
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new JanelaPrincipal(new Ambiente(), desktop.Args?.FirstOrDefault());
        base.OnFrameworkInitializationCompleted();
    }
}
