using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>
/// Assinar Arquivos: assinatura destacada CAdES (.p7s, RFC 5652) com a Política de Assinatura ICP-Brasil
/// (DOC-ICP-15.03) e, se escolhido, carimbo do tempo RFC 3161 de uma ACT (por padrão, a ACT Defesa).
/// </summary>
sealed class PaginaAssinar : PaginaArquivos, IAoMostrar
{
    readonly TextBlock aviso = new() { TextWrapping = TextWrapping.Wrap };
    readonly PainelCredencial credencial;
    readonly ComboBox certificados = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox politica = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox carimbo = new() { Content = "Incluir carimbo do tempo (RFC 3161)" };
    readonly TextBox urlAct = new();
    readonly TextBlock notaPolitica = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };
    List<X509Certificate2> assinantes = new();
    Configuracao? cfgCarregada;

    static readonly (string Rotulo, string Valor)[] Politicas =
    {
        ("AD-RB: referência básica (ICP-Brasil)", "AD-RB"),
        ("AD-RT: referência de tempo, com carimbo do tempo (ICP-Brasil)", "AD-RT"),
        ("Sem política (CMS simples, com a hora do computador)", ""),
    };

    protected override (string Nome, string[] Padroes)[] Filtros => new[] { ("Todos os arquivos", new[] { "*" }) };

    public PaginaAssinar(Ambiente amb) : base(amb)
    {
        credencial = new PainelCredencial(amb, (t, e) => Informar(t, e), "assinar");
        politica.ItemsSource = Politicas.Select(p => p.Rotulo).ToList();
        politica.SelectionChanged += (_, _) => PoliticaMudou();
        carimbo.IsCheckedChanged += (_, _) => urlAct.IsEnabled = carimbo.IsChecked == true;

        Direita.Content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                aviso,
                Ui.Grupo("Credencial do signatário", new StackPanel
                {
                    Spacing = 4, Children = { credencial, Ui.Rotulo("Certificado que vai assinar:"), certificados },
                }),
                Ui.Grupo("Política de assinatura e carimbo do tempo", new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        Ui.Rotulo("Política de Assinatura (DOC-ICP-15.03):"), politica, notaPolitica,
                        carimbo, Ui.Rotulo("Endereço da ACT:"), urlAct,
                        new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8,
                            Text = "A ACT Defesa (" + CarimboTempo.UrlActDefesa + ") só atende computadores das redes cadastradas do MD e das Forças (DPCT ACT Defesa v1.0, item 4.2.3).",
                        },
                    },
                }),
            },
        };
        amb.Mudou += () =>
        {
            if (!ReferenceEquals(cfgCarregada, Amb.Cfg)) CarregarPreferencias(); // só quando as Preferências mudam
            AtualizarCertificados();
        };
        CarregarPreferencias();
        AtualizarCertificados();
        ArquivosMudaram();
    }

    public void AoMostrar()
    {
        credencial.Atualizar();
        AtualizarCertificados();
    }

    void CarregarPreferencias()
    {
        cfgCarregada = Amb.Cfg;
        var i = Array.FindIndex(Politicas, p => p.Valor == Amb.Cfg.PoliticaAssinatura);
        politica.SelectedIndex = i < 0 ? 0 : i;
        carimbo.IsChecked = Amb.Cfg.UsarCarimbo || Amb.Cfg.PoliticaAssinatura == "AD-RT";
        urlAct.Text = Amb.Cfg.UrlAct;
        urlAct.IsEnabled = carimbo.IsChecked == true;
    }

    void PoliticaMudou()
    {
        var valor = Politicas[Math.Max(0, politica.SelectedIndex)].Valor;
        if (valor == "AD-RT") carimbo.IsChecked = true;
        carimbo.IsEnabled = valor != "AD-RT";
        notaPolitica.Text = valor switch
        {
            "AD-RB" => "A política é baixada do ITI (politicas.icpbrasil.gov.br) e conferida pelo resumo SHA-256 publicado; fica guardada para uso sem rede.",
            "AD-RT" => "Exige o carimbo do tempo sobre a assinatura, o que prova que ela existia naquela data e hora.",
            _ => "Assinatura CMS sem política: é verificável, mas não é uma assinatura no padrão ICP-Brasil.",
        };
    }

    void AtualizarCertificados()
    {
        try { assinantes = Amb.Credencial.CertificadosAssinatura(); }
        catch (CryptographicException) { assinantes = new(); }
        certificados.ItemsSource = assinantes.Select(c => $"{CmsCifra.Nome(c)}  (válido até {c.NotAfter:dd/MM/yyyy})").ToList();
        certificados.SelectedIndex = assinantes.Count > 0 ? 0 : -1;
        certificados.PlaceholderText = Amb.Credencial.Conectada ? "Nenhum certificado de assinatura" : "Conecte a credencial";
    }

    protected override void ArquivosMudaram() => aviso.Text = Arquivos.Count == 0
        ? "Utilize o painel da esquerda para adicionar os arquivos que serão assinados. Cada assinatura fica num arquivo .p7s ao lado do original, que não é alterado."
        : $"{Arquivos.Count} arquivo(s) a assinar. A assinatura de cada um será salva como \"nome.extensão.p7s\".";

    protected override async Task Executar()
    {
        if (Arquivos.Count == 0) { Informar("Adicione os arquivos a assinar.", erro: true); return; }
        if (!Amb.Credencial.Conectada) { Informar("Conecte a sua credencial criptográfica (PIN e Conectar).", erro: true); return; }
        if (certificados.SelectedIndex < 0 || certificados.SelectedIndex >= assinantes.Count)
        {
            Informar("Nenhum certificado de assinatura disponível na credencial.", erro: true);
            return;
        }
        var cert = assinantes[certificados.SelectedIndex];
        if (cert.NotAfter < DateTime.Now) { Informar($"O certificado venceu em {cert.NotAfter:dd/MM/yyyy}.", erro: true); return; }
        var valorPolitica = Politicas[Math.Max(0, politica.SelectedIndex)].Valor;
        var act = carimbo.IsChecked == true ? urlAct.Text?.Trim() : null;
        if (carimbo.IsChecked == true && string.IsNullOrEmpty(act)) { Informar("Informe o endereço da ACT.", erro: true); return; }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        PoliticaIcp? pol = null;
        try
        {
            if (valorPolitica != "") pol = await PoliticaIcp.Resolver(valorPolitica, http);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CryptographicException or IOException)
        {
            Informar("Não consegui obter a Política de Assinatura do ITI: " + e.Message, erro: true);
            return;
        }

        var chave = Amb.Credencial.ChaveDe(cert);
        var windows = Amb.Cfg.Modo == ModoCredencial.Windows;
        int feitos = 0, pulados = 0;
        var erros = new List<string>();
        foreach (var arquivo in Arquivos.ToList())
        {
            var destino = arquivo + ".p7s";
            var pode = await PodeGravar(destino);
            if (pode is null) break;
            if (pode == false) { pulados++; continue; }
            try
            {
                var dados = await File.ReadAllBytesAsync(arquivo);
                // no modo Windows a assinatura roda na tela, para o middleware poder pedir o PIN
                var p7s = windows
                    ? await AssinaturaCades.Assinar(dados, cert, null, pol, act, http, null, NuloSeVazio(Amb.Cfg.PoliticaAct), silencioso: false)
                    : await Task.Run(() => AssinaturaCades.Assinar(dados, cert, chave, pol, act, http, null, NuloSeVazio(Amb.Cfg.PoliticaAct)));
                await File.WriteAllBytesAsync(destino, p7s);
                feitos++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException
                                          or HttpRequestException or TaskCanceledException or Net.Pkcs11Interop.Common.Pkcs11Exception)
            {
                erros.Add($"{Path.GetFileName(arquivo)}: {PainelCredencial.TextoErro(e)}");
                if (e is HttpRequestException or TaskCanceledException) break; // a ACT fora do ar falharia em todos
            }
        }
        Amb.DepoisDeUsar();
        var resumo = $"Foram assinados {feitos} arquivo(s)" + (pulados > 0 ? $", {pulados} pulado(s)" : "") +
                     (pol is null ? "" : $", com a política {pol.Nome}") + (act is null ? "" : " e carimbo do tempo") +
                     ". As assinaturas (.p7s) estão na mesma pasta dos originais.";
        Informar(erros.Count == 0 ? resumo : resumo + "\nFalhas:\n" + string.Join("\n", erros), erro: erros.Count > 0);
    }

    static string? NuloSeVazio(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
