using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CifraToken.App;

/// <summary>
/// Janela que pede o PIN do token (ou a senha do .pfx) depois que o usuário escolheu os arquivos e o certificado
/// e clicou em Ok. Com PIN errado a janela continua aberta e mostra o erro.
/// </summary>
sealed class JanelaPin : Window
{
    readonly TextBox pin = new() { PasswordChar = '•' };
    readonly TextBlock erro = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed, IsVisible = false };
    readonly Button ok;
    readonly Func<string, Task<string?>> tentar;
    bool conectou;

    JanelaPin(string rotulo, string oQue, string? aviso, Func<string, Task<string?>> tentar)
    {
        this.tentar = tentar;
        Title = rotulo;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var mostrar = new CheckBox { Content = "Mostrar senha" };
        mostrar.IsCheckedChanged += (_, _) => pin.PasswordChar = mostrar.IsChecked == true ? '\0' : '•';
        pin.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = Confirmar(); };
        ok = Ui.BotaoTexto(Icones.Certo, "Ok", () => _ = Confirmar());
        ok.IsDefault = true;
        var botoes = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, Ui.BotaoTexto(Icones.Xis, "Cancelar", Close) },
        };
        var corpo = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        corpo.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { Icones.Criar(Icones.Token, 28), new TextBlock { Text = oQue, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } },
        });
        if (aviso is not null)
            corpo.Children.Add(new TextBlock { Text = aviso, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        corpo.Children.Add(Ui.Rotulo(rotulo + ":"));
        corpo.Children.Add(pin);
        corpo.Children.Add(mostrar);
        corpo.Children.Add(erro);
        corpo.Children.Add(botoes);
        Content = corpo;
        Opened += (_, _) => pin.Focus();
    }

    async Task Confirmar()
    {
        var p = pin.Text ?? "";
        if (p.Length == 0) { Mostrar("Digite o PIN."); return; }
        ok.IsEnabled = pin.IsEnabled = false;
        Cursor = new Cursor(StandardCursorType.Wait);
        try
        {
            var falha = await tentar(p);
            if (falha is null) { conectou = true; Close(); return; }
            Mostrar(falha);
        }
        finally
        {
            pin.Text = "";
            ok.IsEnabled = pin.IsEnabled = true;
            Cursor = Cursor.Default;
            pin.Focus();
        }
    }

    void Mostrar(string texto)
    {
        erro.Text = texto;
        erro.IsVisible = true;
    }

    /// <summary>
    /// Abre a janela e chama <paramref name="tentar"/> com o PIN digitado; ela devolve null quando conectou
    /// ou a mensagem de erro. Devolve false se o usuário cancelar.
    /// </summary>
    public static async Task<bool> Pedir(Visual dono, string rotulo, string oQue, string? aviso, Func<string, Task<string?>> tentar)
    {
        if (Teste is not null) return await tentar(Teste) is null;
        var janela = new JanelaPin(rotulo, oQue, aviso, tentar);
        await janela.ShowDialog((Window)TopLevel.GetTopLevel(dono)!);
        return janela.conectou;
    }

    /// <summary>PIN usado sem abrir a janela, só nos testes das telas.</summary>
    internal static string? Teste;
}
