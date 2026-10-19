using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>Preferências: credencial (token, Windows ou .pfx), criptografia, assinatura e contatos.</summary>
sealed class JanelaPreferencias : Window
{
    readonly Configuracao cfg;
    readonly RadioButton rToken = new() { Content = "Utilizar token criptográfico em hardware (driver PKCS#11 do fabricante)", GroupName = "cred" };
    readonly RadioButton rWindows = new() { Content = "Utilizar o repositório de certificados do Windows (minidriver / CSP)", GroupName = "cred" };
    readonly RadioButton rPfx = new() { Content = "Utilizar arquivo PFX (certificado A1)", GroupName = "cred" };
    readonly ComboBox drivers = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Drivers encontrados neste computador" };
    readonly TextBox driver = new();
    readonly TextBox pfx = new();
    readonly CheckBox encerrar = new() { Content = "Encerrar a sessão do token após cada uso (mais seguro em computadores compartilhados)" };
    readonly CheckBox oaep = new() { Content = "Usar RSA-OAEP por padrão ao cifrar para destinatários" };
    readonly TextBox pastaContatos = new();
    readonly ComboBox politica = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox carimbo = new() { Content = "Incluir carimbo do tempo por padrão (obrigatório na AD-RT)" };
    readonly TextBox urlAct = new();
    readonly TextBox politicaAct = new() { Watermark = "vazio: a ACT usa a sua política padrão" };
    static readonly string[] Politicas = { "AD-RB", "AD-RT", "" };
    readonly TextBlock resultadoTeste = new() { TextWrapping = TextWrapping.Wrap };

    public JanelaPreferencias(Configuracao cfg)
    {
        this.cfg = cfg;
        Title = "Preferências";
        Width = 760;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var instalados = TokenPkcs11.BibliotecasInstaladas();
        drivers.ItemsSource = instalados.Select(b => $"{b.Fabricante}  —  {b.Caminho}").ToList();
        drivers.SelectionChanged += (_, _) =>
        {
            if (drivers.SelectedIndex >= 0) driver.Text = instalados[drivers.SelectedIndex].Caminho;
        };
        driver.Text = cfg.Driver;
        pfx.Text = cfg.ArquivoPfx;
        encerrar.IsChecked = cfg.EncerrarSessaoAposUso;
        oaep.IsChecked = cfg.UsarOaep;
        pastaContatos.Text = cfg.PastaContatos;
        rWindows.IsVisible = OperatingSystem.IsWindows();
        (cfg.Modo switch { ModoCredencial.Windows when OperatingSystem.IsWindows() => rWindows, ModoCredencial.Pfx => rPfx, _ => rToken }).IsChecked = true;

        var linhaDriver = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        linhaDriver.Children.Add(driver);
        var procurarDriver = Ui.BotaoSimples("Procurar...", () => _ = Procurar(driver, "Driver PKCS#11 do token",
            OperatingSystem.IsWindows() ? new[] { "*.dll" } : new[] { "*.so", "*.so.*" }));
        procurarDriver.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(procurarDriver, 1);
        linhaDriver.Children.Add(procurarDriver);

        var linhaPfx = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        linhaPfx.Children.Add(pfx);
        var procurarPfx = Ui.BotaoSimples("Procurar...", () => _ = Procurar(pfx, "Arquivo de credencial", new[] { "*.pfx", "*.p12" }));
        procurarPfx.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(procurarPfx, 1);
        linhaPfx.Children.Add(procurarPfx);

        var credencial = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(12),
            Children =
            {
                Ui.Rotulo("Acesso à credencial:", negrito: true), rToken, rWindows, rPfx,
                Ui.Rotulo("Driver para acesso ao token criptográfico:", negrito: true),
                drivers, linhaDriver,
                new TextBlock
                {
                    FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                    Text = "Exemplos: Dexon DXSafePKCS11.dll, G+D SafeSign aetpkss1.dll, SafeNet eTPKCS11.dll, Watchdata WDPKCS.dll. " +
                           "O driver vem com o middleware do fabricante. Se o CifraToken for de 64 bits, o driver também precisa ser.",
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.BotaoSimples("Testar a conexão", Testar) } },
                resultadoTeste,
                encerrar,
                Ui.Rotulo("Arquivo de credencial (.pfx):", negrito: true), linhaPfx,
            },
        };

        var criptografia = new StackPanel
        {
            Spacing = 8, Margin = new Thickness(12),
            Children =
            {
                oaep,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                    Text = "Para destinatários: CMS EnvelopedData (RFC 5652) com AES-256-CBC; a chave de sessão é cifrada com o RSA " +
                           "de cada destinatário (PKCS#1 v1.5 por padrão, aceito por todos os tokens).\n\n" +
                           "Com senha: formato do openssl enc e do .enc do Quimera (AES-256-CBC, PBKDF2-HMAC-SHA512, 1.000.000 de iterações).",
                },
            },
        };

        var linhaContatos = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        linhaContatos.Children.Add(pastaContatos);
        var procurarPasta = Ui.BotaoSimples("Procurar...", () => _ = ProcurarPasta());
        procurarPasta.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(procurarPasta, 1);
        linhaContatos.Children.Add(procurarPasta);
        var contatos = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(12),
            Children =
            {
                Ui.Rotulo("Pasta dos contatos (um arquivo .cer por contato):", negrito: true), linhaContatos,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11,
                    Text = "Pode ser uma pasta compartilhada do setor, para que todos usem a mesma agenda.",
                },
            },
        };

        politica.ItemsSource = new[] { "AD-RB: referência básica", "AD-RT: referência de tempo (com carimbo)", "Sem política (CMS simples)" };
        politica.SelectedIndex = Math.Max(0, Array.IndexOf(Politicas, cfg.PoliticaAssinatura));
        carimbo.IsChecked = cfg.UsarCarimbo;
        urlAct.Text = cfg.UrlAct;
        politicaAct.Text = cfg.PoliticaAct;
        var assinatura = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(12),
            Children =
            {
                Ui.Rotulo("Política de Assinatura padrão (DOC-ICP-15.03):", negrito: true), politica,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11,
                    Text = "As políticas CAdES v2.4 (AD-RB 2.16.76.1.7.1.1.2.4 e AD-RT 2.16.76.1.7.1.2.2.4) são baixadas de " +
                           "politicas.icpbrasil.gov.br, conferidas pelo SHA-256 publicado e guardadas em " + PoliticaIcp.PastaCache + ".",
                },
                carimbo,
                Ui.Rotulo("Endereço da ACT (RFC 3161):", negrito: true), urlAct,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { Ui.BotaoSimples("Usar a ACT Defesa", () => { urlAct.Text = CarimboTempo.UrlActDefesa; politicaAct.Text = "2.16.76.1.6.15"; }) },
                },
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11,
                    Text = "A ACT Defesa atende em " + CarimboTempo.UrlActDefesa + " (DPCT v1.0, item 4.2.5) e só aceita pedidos " +
                           "das redes cadastradas do MD e das Forças (item 4.2.3). Fora delas, use outra ACT credenciada na ICP-Brasil.",
                },
                Ui.Rotulo("OID da política da ACT a pedir (opcional):", negrito: true), politicaAct,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11,
                    Text = "Política de Carimbo do Tempo da ACT Defesa: 2.16.76.1.6.15.",
                },
            },
        };

        var abas = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Credencial", FontSize = 14, Content = new ScrollViewer { Content = credencial } },
                new TabItem { Header = "Criptografia", FontSize = 14, Content = criptografia },
                new TabItem { Header = "Assinatura", FontSize = 14, Content = new ScrollViewer { Content = assinatura } },
                new TabItem { Header = "Contatos", FontSize = 14, Content = contatos },
            },
        };
        var botoes = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12),
            Children = { Ui.BotaoTexto(Icones.Certo, "Salvar", Salvar), Ui.BotaoTexto(Icones.Xis, "Cancelar", () => Close(null)) },
        };
        var raiz = new DockPanel();
        DockPanel.SetDock(botoes, Dock.Bottom);
        raiz.Children.Add(botoes);
        raiz.Children.Add(abas);
        Content = raiz;
    }

    async Task Procurar(TextBox alvo, string titulo, string[] padroes)
    {
        var escolhido = (await Ui.AbrirArquivos(this, titulo, false, (titulo, padroes), ("Todos os arquivos", new[] { "*" }))).FirstOrDefault();
        if (escolhido is not null) alvo.Text = escolhido;
    }

    async Task ProcurarPasta()
    {
        if (await Ui.EscolherPasta(this, "Pasta dos contatos") is { } p) pastaContatos.Text = p;
    }

    /// <summary>Lê os certificados do token sem pedir PIN (não gasta tentativas).</summary>
    void Testar()
    {
        var caminho = driver.Text?.Trim() ?? "";
        if (!File.Exists(caminho)) { resultadoTeste.Text = $"O arquivo do driver não foi encontrado em \"{caminho}\"."; return; }
        try
        {
            using var token = new TokenPkcs11(caminho);
            var certs = token.Certificados();
            resultadoTeste.Text = certs.Count == 0
                ? "O driver carregou, mas nenhum certificado foi encontrado. O token está conectado?"
                : "Encontrado(s): " + string.Join("; ", certs.Select(c =>
                    $"{CmsCifra.Nome(c.Certificado)} (token {c.Token}, válido até {c.Certificado.NotAfter:dd/MM/yyyy})"));
        }
        catch (BadImageFormatException)
        {
            resultadoTeste.Text = "O driver é de outra arquitetura (32/64 bits). Use a outra versão do CifraToken (x86 ou x64).";
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or Net.Pkcs11Interop.Common.Pkcs11Exception
                                      or Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException)
        {
            resultadoTeste.Text = "Falha ao acessar o token: " + e.Message;
        }
    }

    void Salvar()
    {
        cfg.Modo = rPfx.IsChecked == true ? ModoCredencial.Pfx : rWindows.IsChecked == true ? ModoCredencial.Windows : ModoCredencial.Token;
        cfg.Driver = driver.Text?.Trim() ?? "";
        cfg.ArquivoPfx = pfx.Text?.Trim() ?? "";
        cfg.EncerrarSessaoAposUso = encerrar.IsChecked == true;
        cfg.UsarOaep = oaep.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(pastaContatos.Text)) cfg.PastaContatos = pastaContatos.Text.Trim();
        cfg.PoliticaAssinatura = Politicas[Math.Max(0, politica.SelectedIndex)];
        cfg.UsarCarimbo = carimbo.IsChecked == true;
        cfg.UrlAct = urlAct.Text?.Trim() ?? "";
        cfg.PoliticaAct = politicaAct.Text?.Trim() ?? "";
        Close(cfg);
    }
}
