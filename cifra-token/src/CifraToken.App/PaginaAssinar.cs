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
    readonly CheckBox carimbo = new() { Content = "Incluir carimbo do tempo (RFC 3161), de uma ACT" };
    readonly TextBox urlAct = new();
    readonly TextBlock notaPolitica = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };
    readonly CheckBox dentroDoPdf = new() { Content = "Assinar dentro do PDF (PAdES), gerando \"nome-assinado.pdf\"" };
    readonly CheckBox mostrarEstampa = new() { Content = "Mostrar a estampa da assinatura na página" };
    readonly TextBlock notaEstampa = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };
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
        credencial = new PainelCredencial(amb, "assinar");
        politica.ItemsSource = Politicas.Select(p => p.Rotulo).ToList();
        politica.SelectionChanged += (_, _) => PoliticaMudou();
        carimbo.IsCheckedChanged += (_, _) => { urlAct.IsEnabled = carimbo.IsChecked == true; EstampaMudou(); };
        mostrarEstampa.IsCheckedChanged += (_, _) => EstampaMudou();
        posicao.ItemsSource = Posicoes.Select(e => e.Rotulo).ToList();
        pagina.ItemsSource = new[] { "Última página", "Primeira página" };
        dentroDoPdf.IsCheckedChanged += (_, _) => EstampaMudou();
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
                dentroDoPdf, mostrarEstampa, notaEstampa, Ui.Rotulo("Página e posição:"), linhaLugar,
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
                Ui.Grupo("Carimbo do tempo e política de assinatura", new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        carimbo, Ui.Rotulo("Endereço da ACT:"), urlAct,
                        new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8,
                            Text = "A ACT Defesa (" + CarimboTempo.UrlActDefesa + ") só atende computadores das redes cadastradas do MD e das Forças (DPCT ACT Defesa v1.0, item 4.2.3).",
                        },
                        Ui.Rotulo("Política de Assinatura (DOC-ICP-15.03):"), politica, notaPolitica,
                    },
                }),
                grupoPdf,
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
        if (!Amb.Credencial.Conectada) Amb.Credencial.Reler(); // o token pode ter sido trocado
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
        mostrarEstampa.IsChecked = Amb.Cfg.MostrarEstampa;
        posicao.SelectedIndex = Math.Max(0, Array.FindIndex(Posicoes, e => e.Valor == Amb.Cfg.PosicaoEstampa));
        pagina.SelectedIndex = Amb.Cfg.PaginaEstampa == 1 ? 1 : 0;
        logo.Text = Amb.Cfg.ArquivoLogo;
        EstampaMudou();
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
        Amb.Cfg.MostrarEstampa = mostrarEstampa.IsChecked == true;
        Amb.Cfg.PosicaoEstampa = Posicoes[Math.Max(0, posicao.SelectedIndex)].Valor;
        Amb.Cfg.PaginaEstampa = pagina.SelectedIndex == 1 ? 1 : 0;
        Amb.Cfg.ArquivoLogo = logo.Text?.Trim() ?? "";
        try { Amb.Cfg.Gravar(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Com carimbo do tempo, a estampa é a redonda "CARIMBADO E ASSINADO"; sem, o quadro de assinatura.</summary>
    void EstampaMudou()
    {
        var dentro = dentroDoPdf.IsChecked == true;
        mostrarEstampa.IsEnabled = dentro;
        pagina.IsEnabled = posicao.IsEnabled = logo.IsEnabled = dentro && mostrarEstampa.IsChecked == true;
        motivo.IsEnabled = dentro;
        notaEstampa.Text = !dentro || mostrarEstampa.IsChecked != true
            ? "A assinatura fica no PDF sem marca visível; os leitores de PDF a mostram no painel de assinaturas."
            : carimbo.IsChecked == true
                ? "Com carimbo do tempo: estampa redonda \"CARIMBADO E ASSINADO\", com a data, o signatário e o logo."
                : "Sem carimbo do tempo: quadro de assinatura com o logo, o signatário e a data.";
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
        var antes = Escolhido?.Thumbprint;
        try { assinantes = Amb.Credencial.CertificadosAssinatura(); }
        catch (CryptographicException) { assinantes = new(); }
        certificados.ItemsSource = assinantes.Select(c => $"{CmsCifra.Nome(c)}  (válido até {c.NotAfter:dd/MM/yyyy})").ToList();
        var i = assinantes.FindIndex(c => c.Thumbprint == antes);
        certificados.SelectedIndex = i >= 0 ? i : assinantes.Count > 0 ? 0 : -1;
        certificados.PlaceholderText = Amb.Cfg.Modo == ModoCredencial.Pfx && !Amb.Credencial.Conectada
            ? "Certificado do arquivo .pfx (a senha é pedida ao clicar em Ok)"
            : "Nenhum certificado de assinatura. O token está conectado?";
    }

    X509Certificate2? Escolhido => certificados.SelectedIndex >= 0 && certificados.SelectedIndex < assinantes.Count
        ? assinantes[certificados.SelectedIndex] : null;

    protected override void ArquivosMudaram()
    {
        var pdfs = Arquivos.Count(EhPdf);
        grupoPdf.IsVisible = pdfs > 0;
        aviso.Text = Arquivos.Count == 0
            ? "Utilize o painel da esquerda para adicionar os arquivos que serão assinados. Cada assinatura fica num arquivo .p7s, " +
              "e o original não é alterado. Os PDFs podem ser assinados por dentro, com estampa e logo. Ao clicar em Ok, " +
              "o programa pergunta a pasta onde gravar e pede o PIN."
            : $"{Arquivos.Count} arquivo(s) a assinar" + (pdfs > 0 ? $", {pdfs} PDF" : "") +
              ". Os demais arquivos ganham a assinatura em \"nome.extensão.p7s\".";
    }

    protected override async Task Executar()
    {
        if (Arquivos.Count == 0) { Informar("Adicione os arquivos a assinar.", erro: true); return; }
        var pfx = Amb.Cfg.Modo == ModoCredencial.Pfx;
        if (Escolhido is null && !(pfx && !Amb.Credencial.Conectada))
        {
            Informar("Nenhum certificado de assinatura encontrado. Confira se o token está conectado e o driver em Preferências.", erro: true);
            Amb.Credencial.Reler();
            AtualizarCertificados();
            return;
        }
        if (Escolhido is { } c0 && c0.NotAfter < DateTime.Now) { Informar($"O certificado venceu em {c0.NotAfter:dd/MM/yyyy}.", erro: true); return; }
        var valorPolitica = Politicas[Math.Max(0, politica.SelectedIndex)].Valor;
        var act = carimbo.IsChecked == true ? urlAct.Text?.Trim() : null;
        if (carimbo.IsChecked == true && string.IsNullOrEmpty(act)) { Informar("Informe o endereço da ACT.", erro: true); return; }

        var pasta = await EscolherPastaSaida("Pasta onde gravar os arquivos assinados");
        if (pasta is null) return;
        var oQue = Escolhido is { } ce ? $"Assinar {Arquivos.Count} arquivo(s) com o certificado:\n{CmsCifra.Nome(ce)} (válido até {ce.NotAfter:dd/MM/yyyy})"
                                       : $"Assinar {Arquivos.Count} arquivo(s) com o certificado do arquivo {Path.GetFileName(Amb.Cfg.ArquivoPfx)}.";
        if (!await AbrirCredencial(oQue)) { Informar("Assinatura cancelada."); return; }
        if (Escolhido is not { } cert) { Informar("A credencial não tem certificado de assinatura.", erro: true); return; }
        if (cert.NotAfter < DateTime.Now) { Informar($"O certificado venceu em {cert.NotAfter:dd/MM/yyyy}.", erro: true); return; }

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

        RSA? chave;
        try { chave = Amb.Credencial.ChaveDe(cert); }
        catch (CryptographicException e) { Informar(e.Message, erro: true); Amb.DepoisDeUsar(); return; }
        var windows = Amb.Cfg.Modo == ModoCredencial.Windows;
        int feitos = 0, pulados = 0;
        var erros = new List<string>();
        foreach (var arquivo in Arquivos.ToList())
        {
            var pdf = comPdf && EhPdf(arquivo);
            var destino = Path.Combine(pasta, pdf ? Path.GetFileNameWithoutExtension(arquivo) + "-assinado.pdf" : Path.GetFileName(arquivo) + ".p7s");
            var pode = await PodeGravar(destino);
            if (pode is null) break;
            if (pode == false) { pulados++; continue; }
            try
            {
                var dados = await File.ReadAllBytesAsync(arquivo);
                var polAct = NuloSeVazio(Amb.Cfg.PoliticaAct);
                var estampa = new Estampa(Amb.Cfg.MostrarEstampa ? EstiloEstampa.Visivel : EstiloEstampa.Nenhuma, Amb.Cfg.PaginaEstampa, Amb.Cfg.PosicaoEstampa, bytesLogo, NuloSeVazio(motivo.Text ?? ""));
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
                     $". Os resultados (.p7s ou -assinado.pdf) estão em {pasta}.";
        Informar(erros.Count == 0 ? resumo : resumo + "\nFalhas:\n" + string.Join("\n", erros), erro: erros.Count > 0);
    }

    static string? NuloSeVazio(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
