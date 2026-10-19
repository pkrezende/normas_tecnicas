using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>
/// Mostra qual credencial será usada nas telas Decifrar e Assinar. Não pede PIN: ele é pedido numa janela
/// própria ao clicar em Ok (veja <see cref="JanelaPin"/>).
/// </summary>
sealed class PainelCredencial : StackPanel
{
    readonly Ambiente amb;
    readonly string acao;
    readonly TextBox acessada = new() { IsReadOnly = true };
    readonly TextBlock nota = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };

    public PainelCredencial(Ambiente amb, string acao)
    {
        this.amb = amb;
        this.acao = acao;
        Spacing = 4;
        Children.AddRange(new Control[] { Ui.Rotulo("Credencial criptográfica:"), acessada, nota });
        amb.Mudou += Atualizar;
        Atualizar();
    }

    public void Atualizar()
    {
        var c = amb.Credencial;
        acessada.Text = c.Conectada ? c.Descricao : c.Origem;
        nota.Text = amb.Cfg.Modo switch
        {
            ModoCredencial.Windows => $"Modo repositório do Windows: o middleware do token pede o PIN no momento de {acao}.",
            ModoCredencial.Pfx => c.Conectada ? "Arquivo aberto." : $"A senha do arquivo é pedida ao clicar em Ok.",
            _ => c.Conectada ? "Token aberto." : $"O PIN é pedido numa janela ao clicar em Ok.",
        } + " Para mudar a credencial, use Preferências > Credencial.";
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
