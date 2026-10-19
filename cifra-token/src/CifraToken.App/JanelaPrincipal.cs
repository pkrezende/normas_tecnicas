using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CifraToken.App;

/// <summary>
/// Janela principal: barra de comandos à esquerda (que se expande com o botão de menu),
/// título da tela no topo e a tela escolhida no centro.
/// </summary>
sealed class JanelaPrincipal : Window
{
    public static readonly string Versao =
        typeof(JanelaPrincipal).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    readonly Ambiente amb;
    readonly ContentControl conteudo = new();
    readonly TextBlock titulo = new() { FontWeight = FontWeight.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock estado = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.8 };
    readonly List<TextBlock> textosNav = new();
    readonly Dictionary<Control, Button> botaoDaTela = new();
    readonly PaginaCifrar cifrar;
    readonly PaginaDecifrar decifrar;
    readonly PaginaContatos contatos;
    readonly PaginaAssinar assinar;
    readonly PaginaVerificar verificar;
    readonly Control inicio;
    bool expandida = true;

    public JanelaPrincipal(Ambiente amb, string? arquivoInicial)
    {
        this.amb = amb;
        Title = $"CifraToken {Versao}";
        Width = 1040;
        Height = 660;
        MinWidth = 780;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        cifrar = new PaginaCifrar(amb, () => Mostrar(contatos!));
        decifrar = new PaginaDecifrar(amb);
        contatos = new PaginaContatos(amb);
        assinar = new PaginaAssinar(amb);
        verificar = new PaginaVerificar(amb);
        inicio = TelaInicial();

        var navTopo = new StackPanel { Spacing = 2 };
        navTopo.Children.Add(ItemNav(Icones.Menu, "Recolher menu", () => Expandir(!expandida), marcar: false));
        navTopo.Children.Add(new Border { Height = 8 });
        navTopo.Children.Add(ItemNav(Icones.Cadeado, "Cifrar Arquivos", () => Mostrar(cifrar), cifrar));
        navTopo.Children.Add(ItemNav(Icones.CadeadoAberto, "Decifrar Arquivos", () => Mostrar(decifrar), decifrar));
        navTopo.Children.Add(ItemNav(Icones.Caneta, "Assinar Arquivos", () => Mostrar(assinar), assinar));
        navTopo.Children.Add(ItemNav(Icones.Selo, "Verificar Assinaturas", () => Mostrar(verificar), verificar));
        navTopo.Children.Add(ItemNav(Icones.Pessoas, "Contatos e Certificados", () => Mostrar(contatos), contatos));

        var navBase = new StackPanel { Spacing = 2 };
        navBase.Children.Add(ItemNav(Icones.Engrenagem, "Preferências", () => _ = AbrirPreferencias(), marcar: false));
        navBase.Children.Add(ItemNav(Icones.Ajuda, "Manual do usuário", () => _ = AbrirManual(), marcar: false));
        navBase.Children.Add(ItemNav(Icones.Info, "Sobre o CifraToken", () => _ = Sobre(), marcar: false));
        navBase.Children.Add(ItemNav(Icones.Desligar, "Sair", Close, marcar: false));

        var nav = new DockPanel { Margin = new Thickness(4) };
        DockPanel.SetDock(navBase, Dock.Bottom);
        nav.Children.Add(navBase);
        nav.Children.Add(navTopo);

        var cabecalho = new DockPanel { Margin = new Thickness(12, 8) };
        var painelEstado = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icones.Criar(Icones.Token, 14), estado } };
        DockPanel.SetDock(painelEstado, Dock.Right);
        cabecalho.Children.Add(painelEstado);
        cabecalho.Children.Add(titulo);

        var direita = new DockPanel();
        DockPanel.SetDock(cabecalho, Dock.Top);
        direita.Children.Add(cabecalho);
        direita.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), BorderThickness = new Thickness(0, 1, 0, 0),
            Child = conteudo,
        });

        var raiz = new DockPanel();
        var barra = new Border { Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)), Child = nav };
        DockPanel.SetDock(barra, Dock.Left);
        raiz.Children.Add(barra);
        raiz.Children.Add(direita);
        Content = raiz;

        amb.Mudou += AtualizarEstado;
        AtualizarEstado();
        Mostrar(inicio);
        Closed += (_, _) => amb.Dispose();

        if (arquivoInicial is not null && File.Exists(arquivoInicial))
        {
            var ext = Path.GetExtension(arquivoInicial).ToLowerInvariant();
            if (ext is ".p7m" or ".enc") { Mostrar(decifrar); decifrar.Adicionar(new[] { arquivoInicial }); }
            else if (ext is ".p7s") { Mostrar(verificar); verificar.Adicionar(new[] { arquivoInicial }); }
            else { Mostrar(cifrar); cifrar.Adicionar(new[] { arquivoInicial }); }
        }
    }

    Button ItemNav(string icone, string texto, Action acao, Control? tela = null, bool marcar = true)
    {
        var rotulo = new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center };
        textosNav.Add(rotulo);
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Icones.Criar(icone, 20), rotulo } },
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent, Padding = new Thickness(8, 6), MinWidth = 40,
        };
        ToolTip.SetTip(b, texto);
        b.Click += (_, _) => acao();
        if (tela is not null) botaoDaTela[tela] = b;
        return b;
    }

    void Expandir(bool sim)
    {
        expandida = sim;
        foreach (var t in textosNav) t.IsVisible = sim;
    }

    void Mostrar(Control tela)
    {
        conteudo.Content = tela;
        titulo.Text = tela switch
        {
            PaginaCifrar => "Cifrar Arquivos",
            PaginaDecifrar => "Decifrar Arquivos",
            PaginaContatos => "Contatos e Certificados",
            PaginaAssinar => "Assinar Arquivos",
            PaginaVerificar => "Verificar Assinaturas",
            _ => "Escolha uma opção para utilizar",
        };
        foreach (var (t, b) in botaoDaTela)
            b.Background = t == tela ? new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)) : Brushes.Transparent;
        if (tela is IAoMostrar m) m.AoMostrar();
    }

    void AtualizarEstado()
    {
        var c = amb.Credencial;
        estado.Text = amb.Cfg.Modo switch
        {
            ModoCredencial.Windows => "Credencial: repositório do Windows",
            _ when c.Conectada => "Conectado: " + c.Descricao,
            ModoCredencial.Token => "Token: " + (string.IsNullOrEmpty(amb.Cfg.Driver) ? "driver não configurado" : Path.GetFileName(amb.Cfg.Driver) + " (desconectado)"),
            _ => "Arquivo .pfx (desconectado)",
        };
    }

    Control TelaInicial()
    {
        Button Bloco(string icone, string texto, string detalhe, Func<Control> tela)
        {
            var b = new Button
            {
                Width = 220, Height = 120, Margin = new Thickness(8),
                Content = new StackPanel
                {
                    Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        Icones.Criar(icone, 32),
                        new TextBlock { Text = texto, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = detalhe, FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    },
                },
            };
            b.Click += (_, _) => Mostrar(tela());
            return b;
        }
        return new StackPanel
        {
            Margin = new Thickness(24), Spacing = 10,
            Children =
            {
                new WrapPanel
                {
                    Children =
                    {
                        Bloco(Icones.Cadeado, "Cifrar Arquivos", "Para destinatários (certificado) ou com senha", () => cifrar),
                        Bloco(Icones.CadeadoAberto, "Decifrar Arquivos", "Com o seu token, .pfx ou a senha", () => decifrar),
                        Bloco(Icones.Caneta, "Assinar Arquivos", "Assinatura ICP-Brasil (.p7s ou dentro do PDF) com carimbo do tempo", () => assinar),
                        Bloco(Icones.Selo, "Verificar Assinaturas", "Confere signatário, política e carimbo do tempo", () => verificar),
                        Bloco(Icones.Pessoas, "Contatos e Certificados", "Certificados de quem pode receber arquivos cifrados", () => contatos),
                    },
                },
                new TextBlock
                {
                    Margin = new Thickness(8, 12, 8, 0), Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                    Text = "Algoritmos abertos: AES-256, RSA e SHA-256 no formato CMS (RFC 5652), compatível com o openssl. " +
                           "Assinatura no padrão CAdES da ICP-Brasil (DOC-ICP-15.03) e carimbo do tempo RFC 3161. " +
                           "Funciona com tokens de qualquer fabricante pelo driver PKCS#11 (Dexon, SafeSign, SafeNet, Watchdata e outros). " +
                           "Não use para informação classificada.",
                },
            },
        };
    }

    async Task AbrirPreferencias()
    {
        var nova = await new JanelaPreferencias(amb.Cfg.Copia()).ShowDialog<Configuracao?>(this);
        if (nova is null) return;
        amb.TrocarConfiguracao(nova);
    }

    async Task AbrirManual()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "LEIA-ME.md");
        if (File.Exists(caminho) && Launcher is { } l && await l.LaunchFileInfoAsync(new FileInfo(caminho))) return;
        await Ui.Mensagem(this, "O manual (LEIA-ME.md) fica na mesma pasta do CifraToken e também no repositório do projeto.");
    }

    Task Sobre() => Ui.Mensagem(this,
        $"CifraToken {Versao}\n\n" +
        "Cifra, decifra e assina arquivos com certificado digital ICP-Brasil em token, .pfx ou senha.\n\n" +
        "Para destinatários: CMS EnvelopedData (RFC 5652), AES-256-CBC com chave de sessão em RSA (PKCS#1 v1.5 ou OAEP).\n" +
        "Com senha: formato do openssl enc / .enc do Quimera (AES-256-CBC, PBKDF2-HMAC-SHA512, 1.000.000 de iterações).\n" +
        "Assinatura: CAdES destacada (.p7s) e PAdES no PDF, com SHA-256, políticas AD-RB e AD-RT da ICP-Brasil (DOC-ICP-15.03) e carimbo do tempo RFC 3161.\n\n" +
        "Bibliotecas: .NET, Avalonia (MIT), Pkcs11Interop (Apache 2.0), PDFsharp 6.2.4 (MIT). Fontes Liberation (SIL OFL 1.1).\n" +
        "Ferramenta consultiva. Não use para informação classificada (Decreto 7.845/2012).",
        "Sobre o CifraToken");
}

/// <summary>Telas que precisam se atualizar quando aparecem.</summary>
interface IAoMostrar
{
    void AoMostrar();
}
