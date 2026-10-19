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
    readonly CheckBox dentroDoPdf = new() { Content = "Assinar dentro do PDF (PAdES), gerando \"nome-assinado.pdf\"" };
    readonly ComboBox estilo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox pagina = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox posicao = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox logo = new() { Watermark = "sem logo" };
    readonly TextBox motivo = new() { Watermark = "opcional, por exemplo: Aprovo o documento" };
    readonly Border grupoPdf;
    List<X509Certificate2> assinantes = new();
    Configuracao? cfgCarregada;

    static readonly (string Rotulo, string Valor)[] Politicas =
    {
        ("AD-RB: referência básica (ICP-Brasil)", "AD-RB"),
        ("AD-RT: referência de tempo, com carimbo do tempo (ICP-Brasil)", "AD-RT"),
        ("Sem política (CMS simples, com a hora do computador)", ""),
    };

    static readonly (string Rotulo, EstiloEstampa Valor)[] Estilos =
    {
        ("Carimbo (à moda antiga, inclinado, com a data)", EstiloEstampa.Carimbo),
        ("Quadro de assinatura (logo, nome e data)", EstiloEstampa.Assinatura),
        ("Sem estampa visível", EstiloEstampa.Nenhuma),
    };
    static readonly (string Rotulo, PosicaoEstampa Valor)[] Posicoes =
    {
        ("Canto inferior direito", PosicaoEstampa.InferiorDireito), ("Canto inferior esquerdo", PosicaoEstampa.InferiorEsquerdo),
        ("Canto superior direito", PosicaoEstampa.SuperiorDireito), ("Canto superior esquerdo", PosicaoEstampa.SuperiorEsquerdo),
        ("Centro da página", PosicaoEstampa.Centro),
    };

    protected override (string Nome, string[] Padroes)[] Filtros => new[]
    {
        ("Todos os arquivos", new[] { "*" }),
        ("Documentos PDF (*.pdf)", new[] { "*.pdf" }),
    };

    public PaginaAssinar(Ambiente amb) : base(amb)
    {
        credencial = new PainelCredencial(amb, (t, e) => Informar(t, e), "assinar");
        politica.ItemsSource = Politicas.Select(p => p.Rotulo).ToList();
        politica.SelectionChanged += (_, _) => PoliticaMudou();
        carimbo.IsCheckedChanged += (_, _) => urlAct.IsEnabled = carimbo.IsChecked == true;
        estilo.ItemsSource = Estilos.Select(e => e.Rotulo).ToList();
        posicao.ItemsSource = Posicoes.Select(e => e.Rotulo).ToList();
        pagina.ItemsSource = new[] { "Última página", "Primeira página" };
        dentroDoPdf.IsCheckedChanged += (_, _) => estilo.IsEnabled = pagina.IsEnabled = posicao.IsEnabled = logo.IsEnabled = motivo.IsEnabled = dentroDoPdf.IsChecked == true;
        var linhaLogo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        linhaLogo.Children.Add(logo);
        var procurar = Ui.BotaoSimples("Procurar...", () => _ = EscolherLogo());
        procurar.Margin = new Avalonia.Thickness(6, 0, 0, 0);
        Grid.SetColumn(procurar, 1);
        linhaLogo.Children.Add(procurar);
        var semLogo = Ui.Botao(Icones.Xis, "Sem logo", () => logo.Text = "");
        Grid.SetColumn(semLogo, 2);
        linhaLogo.Children.Add(semLogo);
        var linhaLugar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        linhaLugar.Children.Add(pagina);
        Grid.SetColumn(posicao, 2);
        linhaLugar.Children.Add(posicao);
        grupoPdf = Ui.Grupo("Documentos PDF", new StackPanel
        {
            Spacing = 4,
            Children =
            {
                dentroDoPdf, Ui.Rotulo("Estampa na página:"), estilo, Ui.Rotulo("Página e posição:"), linhaLugar,
                Ui.Rotulo("Logo (PNG ou JPEG):"), linhaLogo, Ui.Rotulo("Motivo:"), motivo,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8,
                    Text = "Política PAdES v1.3 da ICP-Brasil (IN ITI nº 34/2025). A estampa é desenhada antes da assinatura e fica protegida por ela. " +
                           "A hora da estampa é a do computador; a hora oficial é a do carimbo do tempo, mostrada na verificação.",
                },
            },
        });

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
                grupoPdf,
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
        dentroDoPdf.IsChecked = Amb.Cfg.AssinarDentroDoPdf;
        estilo.SelectedIndex = Math.Max(0, Array.FindIndex(Estilos, e => e.Valor == Amb.Cfg.Estampa));
        posicao.SelectedIndex = Math.Max(0, Array.FindIndex(Posicoes, e => e.Valor == Amb.Cfg.PosicaoEstampa));
        pagina.SelectedIndex = Amb.Cfg.PaginaEstampa == 1 ? 1 : 0;
        logo.Text = Amb.Cfg.ArquivoLogo;
    }

    async Task EscolherLogo()
    {
        var escolhido = (await Ui.AbrirArquivos(this, "Logo para a estampa", false, ("Imagens (*.png, *.jpg)", new[] { "*.png", "*.jpg", "*.jpeg" }))).FirstOrDefault();
        if (escolhido is not null) logo.Text = escolhido;
    }

    static bool EhPdf(string caminho) => caminho.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Guarda as escolhas de PDF para a próxima vez.</summary>
    void LembrarEscolhas()
    {
        Amb.Cfg.AssinarDentroDoPdf = dentroDoPdf.IsChecked == true;
        Amb.Cfg.Estampa = Estilos[Math.Max(0, estilo.SelectedIndex)].Valor;
        Amb.Cfg.PosicaoEstampa = Posicoes[Math.Max(0, posicao.SelectedIndex)].Valor;
        Amb.Cfg.PaginaEstampa = pagina.SelectedIndex == 1 ? 1 : 0;
        Amb.Cfg.ArquivoLogo = logo.Text?.Trim() ?? "";
        try { Amb.Cfg.Gravar(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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

    protected override void ArquivosMudaram()
    {
        var pdfs = Arquivos.Count(EhPdf);
        grupoPdf.IsVisible = pdfs > 0;
        aviso.Text = Arquivos.Count == 0
            ? "Utilize o painel da esquerda para adicionar os arquivos que serão assinados. Cada assinatura fica num arquivo .p7s ao lado do original, " +
              "que não é alterado. Os PDFs podem ser assinados por dentro, com estampa de carimbo e logo."
            : $"{Arquivos.Count} arquivo(s) a assinar" + (pdfs > 0 ? $", {pdfs} PDF" : "") +
              ". Os demais arquivos ganham a assinatura em \"nome.extensão.p7s\".";
    }

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
        var comPdf = dentroDoPdf.IsChecked == true && Arquivos.Any(EhPdf);
        var comP7s = Arquivos.Any(a => !(comPdf && EhPdf(a)));
        byte[]? bytesLogo = null;
        if (comPdf && !string.IsNullOrWhiteSpace(logo.Text))
        {
            try { bytesLogo = await File.ReadAllBytesAsync(logo.Text.Trim()); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Informar("Não consegui ler o logo: " + e.Message, erro: true); return; }
        }
        if (comPdf) LembrarEscolhas();
        PoliticaIcp? pol = null, polPdf = null;
        try
        {
            if (valorPolitica != "" && comP7s) pol = await PoliticaIcp.Resolver(valorPolitica, http);
            if (valorPolitica != "" && comPdf) polPdf = await PoliticaIcp.Resolver("PAdES " + valorPolitica, http);
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
            var pdf = comPdf && EhPdf(arquivo);
            var destino = pdf ? Path.Combine(Path.GetDirectoryName(arquivo)!, Path.GetFileNameWithoutExtension(arquivo) + "-assinado.pdf") : arquivo + ".p7s";
            var pode = await PodeGravar(destino);
            if (pode is null) break;
            if (pode == false) { pulados++; continue; }
            try
            {
                var dados = await File.ReadAllBytesAsync(arquivo);
                var polAct = NuloSeVazio(Amb.Cfg.PoliticaAct);
                var estampa = new Estampa(Amb.Cfg.Estampa, Amb.Cfg.PaginaEstampa, Amb.Cfg.PosicaoEstampa, bytesLogo, NuloSeVazio(motivo.Text ?? ""));
                // no modo Windows a assinatura roda na tela, para o middleware poder pedir o PIN
                Func<Task<byte[]>> assinar = pdf
                    ? () => AssinaturaPdf.Assinar(dados, cert, chave, polPdf, act, http, estampa, null, polAct, silencioso: !windows)
                    : () => AssinaturaCades.Assinar(dados, cert, chave, pol, act, http, null, polAct, silencioso: !windows);
                var resultado = windows ? await assinar() : await Task.Run(assinar);
                await File.WriteAllBytesAsync(destino, resultado);
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
                     ((pol ?? polPdf) is null ? "" : ", com a política ICP-Brasil " + (pol is null ? "" : pol.Nome) +
                      (pol is not null && polPdf is not null ? " e " : "") + (polPdf is null ? "" : polPdf.Nome)) +
                     (act is null ? "" : ", com carimbo do tempo") +
                     ". Os resultados (.p7s ou -assinado.pdf) estão na mesma pasta dos originais.";
        Informar(erros.Count == 0 ? resumo : resumo + "\nFalhas:\n" + string.Join("\n", erros), erro: erros.Count > 0);
    }

    static string? NuloSeVazio(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
