using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>PIN, botão Conectar e a credencial acessada, usados nas telas Decifrar e Assinar.</summary>
sealed class PainelCredencial : StackPanel
{
    readonly Ambiente amb;
    readonly Action<string, bool> informar;
    readonly TextBlock rotuloPin = Ui.Rotulo("PIN para acesso à credencial:");
    readonly TextBox pin = new() { PasswordChar = '•' };
    readonly Button conectar;
    readonly TextBox acessada = new() { IsReadOnly = true };
    readonly TextBlock notaWindows = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };

    public PainelCredencial(Ambiente amb, Action<string, bool> informar, string acao)
    {
        this.amb = amb;
        this.informar = informar;
        Spacing = 4;
        conectar = Ui.BotaoSimples("Conectar", () => _ = Conectar());
        conectar.VerticalAlignment = VerticalAlignment.Stretch;
        conectar.Margin = new Thickness(8, 0, 0, 0);
        var linhaPin = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        linhaPin.Children.Add(pin);
        Grid.SetColumn(conectar, 1);
        linhaPin.Children.Add(conectar);
        var mostrarPin = new CheckBox { Content = "Mostrar senha" };
        mostrarPin.IsCheckedChanged += (_, _) => pin.PasswordChar = mostrarPin.IsChecked == true ? '\0' : '•';
        pin.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) _ = Conectar(); };
        notaWindowsTexto = $"Modo repositório do Windows: o middleware do token pede o PIN no momento de {acao}. Para mudar, use Preferências > Credencial.";
        Children.AddRange(new Control[] { rotuloPin, linhaPin, mostrarPin, Ui.Rotulo("Credencial criptográfica acessada:"), acessada, notaWindows });
        amb.Mudou += Atualizar;
        Atualizar();
    }

    readonly string notaWindowsTexto;

    public void Atualizar()
    {
        var c = amb.Credencial;
        rotuloPin.Text = c.RotuloSenha;
        pin.IsEnabled = conectar.IsEnabled = c.PrecisaConectar;
        acessada.Text = c.Conectada ? c.Descricao : "";
        notaWindows.Text = c.PrecisaConectar ? "" : notaWindowsTexto;
    }

    async Task Conectar()
    {
        try
        {
            var alerta = await Task.Run(amb.Credencial.AvisoPin);
            if (alerta is not null && !await Ui.Confirmar(this, alerta + "\n\nContinuar?", "Atenção ao PIN")) return;
            var p = pin.Text ?? "";
            await Task.Run(() => amb.Credencial.Conectar(p));
            informar("Credencial conectada: " + amb.Credencial.Descricao, false);
        }
        catch (Exception e) when (e is CryptographicException or IOException or Net.Pkcs11Interop.Common.Pkcs11Exception
                                      or Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException or BadImageFormatException)
        {
            informar(TextoErro(e), true);
        }
        finally
        {
            pin.Text = "";
            amb.Avisar();
        }
    }

    public static string TextoErro(Exception e) => e switch
    {
        Net.Pkcs11Interop.Common.Pkcs11Exception p => TokenPkcs11.Mensagem(p.RV),
        BadImageFormatException => "O driver do token é de outra arquitetura (32/64 bits). Use a outra versão do CifraToken (x86 ou x64).",
        Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException => "Não consegui carregar o driver do token: " + e.Message,
        HttpRequestException or TaskCanceledException => "Não consegui falar com a ACT: " + e.Message +
            " A ACT Defesa só atende computadores das redes cadastradas do MD e das Forças.",
        _ => e.Message,
    };
}
