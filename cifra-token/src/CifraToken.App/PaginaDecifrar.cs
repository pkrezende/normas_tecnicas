using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>
/// Decifrar Arquivos: pede a senha para os .enc e, ao clicar em Ok, a pasta de saída e o PIN da credencial
/// (token ou .pfx) para os .p7m; os decifrados recebem o nome original.
/// </summary>
sealed class PaginaDecifrar : PaginaArquivos, IAoMostrar
{
    readonly TextBlock aviso = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBox senha = new() { PasswordChar = '•' };
    readonly Border grupoSenha;
    readonly PainelCredencial credencial;
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

        credencial = new PainelCredencial(amb, "decifrar");
        grupoToken = Ui.Grupo("Arquivo(s) cifrado(s) para destinatário(s)", credencial);

        Direita.Content = new StackPanel { Spacing = 4, Children = { aviso, grupoSenha, grupoToken } };
        ArquivosMudaram();
    }

    public void AoMostrar() => credencial.Atualizar();


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
                : "Os arquivos selecionados foram cifrados para destinatários: ao clicar em Ok, o PIN da sua credencial é pedido.";
    }

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
        var pasta = await EscolherPastaSaida("Pasta onde gravar os arquivos decifrados");
        if (pasta is null) return;
        var p7m = Arquivos.Where(a => !EhArquivoComSenha(a)).ToList();
        if (p7m.Count > 0 && !await AbrirCredencial(DescreverP7m(p7m))) { Informar("Operação cancelada."); return; }

        var s = senha.Text ?? "";
        int feitos = 0, pulados = 0;
        var erros = new List<string>();
        foreach (var arquivo in Arquivos.ToList())
        {
            var destino = Path.Combine(pasta, Path.GetFileName(NomeOriginal(arquivo)));
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
                erros.Add($"{Path.GetFileName(arquivo)}: {PainelCredencial.TextoErro(e)}");
            }
        }
        Amb.DepoisDeUsar();
        var resumo = $"Foram decifrados {feitos} arquivo(s)" + (pulados > 0 ? $", {pulados} pulado(s)" : "") +
                     $", salvos em {pasta} com o nome original.";
        Informar(erros.Count == 0 ? resumo : resumo + "\nFalhas:\n" + string.Join("\n", erros), erro: erros.Count > 0);
        senha.Text = "";
    }

    /// <summary>Texto da janela do PIN: os arquivos e para quem foram cifrados.</summary>
    static string DescreverP7m(List<string> arquivos)
    {
        var texto = arquivos.Count == 1 ? $"Decifrar {Path.GetFileName(arquivos[0])}" : $"Decifrar {arquivos.Count} arquivos";
        try
        {
            var para = CmsCifra.Destinatarios(File.ReadAllBytes(arquivos[0])).Select(d => d.Resumo).Distinct().Take(3).ToList();
            if (para.Count > 0) texto += ", cifrado para:\n" + string.Join("\n", para);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException) { }
        return texto + ".";
    }

    /// <summary>"relatorio.pdf.p7m" vira "relatorio.pdf"; sem extensão conhecida, acrescenta ".decifrado".</summary>
    static string NomeOriginal(string arquivo)
    {
        var ext = Path.GetExtension(arquivo).ToLowerInvariant();
        return ext is ".p7m" or ".enc" ? arquivo[..^ext.Length] : arquivo + ".decifrado";
    }
}
