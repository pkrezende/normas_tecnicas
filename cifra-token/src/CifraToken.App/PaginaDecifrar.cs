using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>
/// Decifrar Arquivos: pede a senha para os .enc e a conexão da credencial (token, Windows ou .pfx)
/// para os .p7m; os decifrados são salvos ao lado dos cifrados, com o nome original.
/// </summary>
sealed class PaginaDecifrar : PaginaArquivos, IAoMostrar
{
    readonly TextBlock aviso = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBox senha = new() { PasswordChar = '•' };
    readonly Border grupoSenha;
    readonly TextBlock rotuloPin = Ui.Rotulo("PIN para acesso à credencial:");
    readonly TextBox pin = new() { PasswordChar = '•' };
    readonly Button conectar;
    readonly TextBox acessada = new() { IsReadOnly = true };
    readonly TextBlock notaWindows = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };
    readonly Border grupoToken;

    protected override (string Nome, string[] Padroes)[] Filtros => new[]
    {
        ("Arquivos cifrados (*.p7m, *.enc)", new[] { "*.p7m", "*.enc" }),
        ("Todos os arquivos", new[] { "*" }),
    };

    public PaginaDecifrar(Ambiente amb) : base(amb)
    {
        var mostrarSenha = new CheckBox { Content = "Mostrar senha" };
        mostrarSenha.IsCheckedChanged += (_, _) => senha.PasswordChar = mostrarSenha.IsChecked == true ? '\0' : '•';
        grupoSenha = Ui.Grupo("Arquivo(s) cifrado(s) com senha", new StackPanel
        {
            Spacing = 4, Children = { Ui.Rotulo("Digite a senha para decifrar o(s) arquivo(s):"), senha, mostrarSenha },
        });

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
        grupoToken = Ui.Grupo("Arquivo(s) cifrado(s) para destinatário(s)", new StackPanel
        {
            Spacing = 4,
            Children = { rotuloPin, linhaPin, mostrarPin, Ui.Rotulo("Credencial criptográfica acessada:"), acessada, notaWindows },
        });

        Direita.Content = new StackPanel { Spacing = 4, Children = { aviso, grupoSenha, grupoToken } };
        amb.Mudou += AtualizarCredencial;
        AtualizarCredencial();
        ArquivosMudaram();
    }

    public void AoMostrar() => AtualizarCredencial();

    void AtualizarCredencial()
    {
        var c = Amb.Credencial;
        rotuloPin.Text = c.RotuloSenha;
        pin.IsEnabled = conectar.IsEnabled = c.PrecisaConectar;
        acessada.Text = c.Conectada ? c.Descricao : "";
        notaWindows.Text = c.PrecisaConectar ? "" :
            "Modo repositório do Windows: o middleware do token pede o PIN no momento de decifrar. Para mudar, use Preferências > Credencial.";
    }

    protected override void ArquivosMudaram()
    {
        var comSenha = Arquivos.Count(EhArquivoComSenha);
        var comToken = Arquivos.Count - comSenha;
        grupoSenha.IsEnabled = comSenha > 0;
        grupoToken.IsEnabled = comToken > 0;
        aviso.Text = Arquivos.Count == 0
            ? "Utilize o painel da esquerda para adicionar os arquivos que serão decifrados (.p7m ou .enc)."
            : comSenha > 0 && comToken > 0
                ? "Os arquivos selecionados usam tipos de criptografia diferentes: alguns precisam da senha e outros da sua chave privada (credencial criptográfica)."
                : comSenha > 0 ? "Os arquivos selecionados foram cifrados com senha."
                : "Os arquivos selecionados foram cifrados para destinatários: conecte a sua credencial criptográfica.";
    }

    async Task Conectar()
    {
        try
        {
            var alerta = await Task.Run(Amb.Credencial.AvisoPin);
            if (alerta is not null && !await Ui.Confirmar(this, alerta + "\n\nContinuar?", "Atenção ao PIN")) return;
            var p = pin.Text ?? "";
            await Task.Run(() => Amb.Credencial.Conectar(p));
            Informar("Credencial conectada: " + Amb.Credencial.Descricao);
        }
        catch (Exception e) when (e is CryptographicException or IOException or Net.Pkcs11Interop.Common.Pkcs11Exception
                                      or Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException or BadImageFormatException)
        {
            Informar(TextoErro(e), erro: true);
        }
        finally
        {
            pin.Text = "";
            Amb.Avisar();
        }
    }

    static string TextoErro(Exception e) => e switch
    {
        Net.Pkcs11Interop.Common.Pkcs11Exception p => TokenPkcs11.Mensagem(p.RV),
        BadImageFormatException => "O driver do token é de outra arquitetura (32/64 bits). Use a outra versão do CifraToken (x86 ou x64).",
        Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException => "Não consegui carregar o driver do token: " + e.Message,
        _ => e.Message,
    };

    protected override void Limpar()
    {
        base.Limpar();
        senha.Text = "";
    }

    protected override async Task Executar()
    {
        if (Arquivos.Count == 0) { Informar("Adicione os arquivos a decifrar.", erro: true); return; }
        var temSenha = Arquivos.Any(EhArquivoComSenha);
        if (temSenha && string.IsNullOrEmpty(senha.Text)) { Informar("Digite a senha dos arquivos cifrados com senha.", erro: true); return; }
        if (Arquivos.Any(a => !EhArquivoComSenha(a)) && !Amb.Credencial.Conectada)
        {
            Informar("Conecte a sua credencial criptográfica (PIN e Conectar) para os arquivos cifrados para destinatários.", erro: true);
            return;
        }

        var s = senha.Text ?? "";
        int feitos = 0, pulados = 0;
        var erros = new List<string>();
        foreach (var arquivo in Arquivos.ToList())
        {
            var destino = NomeOriginal(arquivo);
            var pode = await PodeGravar(destino);
            if (pode is null) break;
            if (pode == false) { pulados++; continue; }
            try
            {
                var dados = await File.ReadAllBytesAsync(arquivo);
                byte[] claro;
                if (SenhaOpenSsl.EhFormatoOpenSsl(dados)) claro = await Task.Run(() => SenhaOpenSsl.Decifrar(dados, s));
                else if (Amb.Cfg.Modo == ModoCredencial.Windows) claro = Amb.Credencial.Decifrar(dados); // o PIN do middleware precisa da tela
                else claro = await Task.Run(() => Amb.Credencial.Decifrar(dados));
                await File.WriteAllBytesAsync(destino, claro);
                feitos++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or FormatException
                                          or Net.Pkcs11Interop.Common.Pkcs11Exception)
            {
                erros.Add($"{Path.GetFileName(arquivo)}: {TextoErro(e)}");
            }
        }
        Amb.DepoisDeUsar();
        var resumo = $"Foram decifrados {feitos} arquivo(s)" + (pulados > 0 ? $", {pulados} pulado(s)" : "") +
                     ", salvos na mesma pasta dos cifrados, com o nome original.";
        Informar(erros.Count == 0 ? resumo : resumo + "\nFalhas:\n" + string.Join("\n", erros), erro: erros.Count > 0);
        senha.Text = "";
    }

    /// <summary>"relatorio.pdf.p7m" vira "relatorio.pdf"; sem extensão conhecida, acrescenta ".decifrado".</summary>
    static string NomeOriginal(string arquivo)
    {
        var ext = Path.GetExtension(arquivo).ToLowerInvariant();
        return ext is ".p7m" or ".enc" ? arquivo[..^ext.Length] : arquivo + ".decifrado";
    }
}
