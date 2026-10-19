using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>
/// Contatos e Certificados: agenda com os certificados públicos dos destinatários, com os detalhes
/// do certificado selecionado, importação de .cer e exportação do seu certificado para enviar a outros.
/// </summary>
sealed class PaginaContatos : UserControl, IAoMostrar
{
    readonly Ambiente amb;
    readonly ListBox lista = new();
    readonly TextBox busca = new() { Watermark = "Buscar", Width = 200 };
    readonly StackPanel detalhes = new() { Spacing = 2 };
    readonly TextBlock mensagem = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4) };
    List<X509Certificate2> todos = new();

    public PaginaContatos(Ambiente amb)
    {
        this.amb = amb;
        var barra = new WrapPanel
        {
            Margin = new Thickness(8, 8, 8, 4),
            Children =
            {
                Ui.BotaoTexto(Icones.Token, "Adicionar meu certificado", () => _ = AdicionarMeu()),
                Ui.BotaoTexto(Icones.Mais, "Importar .cer", () => _ = Importar()),
                Ui.BotaoTexto(Icones.Lupa, "Do token / Windows...", () => _ = DoRepositorio()),
                Ui.BotaoTexto(Icones.Exportar, "Exportar .cer", () => _ = Exportar()),
                Ui.BotaoTexto(Icones.Lixeira, "Remover", () => _ = Remover()),
                Ui.BotaoTexto(Icones.Pasta, "Abrir pasta", () => _ = AbrirPasta()),
            },
        };
        foreach (var c in barra.Children) c.Margin = new Thickness(0, 0, 6, 4);
        busca.TextChanged += (_, _) => Preencher();
        lista.SelectionChanged += (_, _) => MostrarDetalhes(lista.SelectedItem as ListBoxItem);

        var esquerda = new DockPanel { Margin = new Thickness(8) };
        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 4), Children = { busca } };
        DockPanel.SetDock(busca, Dock.Right);
        topo.Children.Add(new TextBlock { Text = "Contatos:", VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(topo, Dock.Top);
        esquerda.Children.Add(topo);
        esquerda.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)), BorderThickness = new Thickness(1), Child = lista,
        });

        var direita = new Border
        {
            Margin = new Thickness(8), Padding = new Thickness(10), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            Child = new ScrollViewer { Content = detalhes },
        };
        var corpo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,420") };
        corpo.Children.Add(esquerda);
        var divisor = new GridSplitter();
        Grid.SetColumn(divisor, 1);
        corpo.Children.Add(divisor);
        Grid.SetColumn(direita, 2);
        corpo.Children.Add(direita);

        var raiz = new DockPanel();
        DockPanel.SetDock(barra, Dock.Top);
        DockPanel.SetDock(mensagem, Dock.Bottom);
        raiz.Children.Add(barra);
        raiz.Children.Add(mensagem);
        raiz.Children.Add(corpo);
        Content = raiz;
        amb.Mudou += AoMostrar;
    }

    public void AoMostrar()
    {
        todos = amb.Agenda.Todos();
        Preencher();
    }

    void Preencher()
    {
        var filtro = busca.Text?.Trim() ?? "";
        lista.Items.Clear();
        foreach (var c in todos.Where(c => filtro.Length == 0 || CmsCifra.Nome(c).Contains(filtro, StringComparison.CurrentCultureIgnoreCase)))
        {
            var vencido = c.NotAfter < DateTime.Now;
            lista.Items.Add(new ListBoxItem
            {
                Tag = c,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = CmsCifra.Nome(c), FontWeight = FontWeight.SemiBold },
                        Subtitulo(c, vencido),
                    },
                },
            });
        }
        detalhes.Children.Clear();
        detalhes.Children.Add(new TextBlock
        {
            Text = todos.Count == 0
                ? "Nenhum contato ainda. Comece por \"Adicionar meu certificado\" e depois importe o .cer de quem vai receber os arquivos."
                : "Selecione um contato para ver os detalhes do certificado.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
        });
    }

    static TextBlock Subtitulo(X509Certificate2 c, bool vencido)
    {
        var t = new TextBlock
        {
            FontSize = 11, Opacity = 0.75,
            Text = $"{c.GetNameInfo(X509NameType.SimpleName, true)} · válido até {c.NotAfter:dd/MM/yyyy}" +
                   (vencido ? " · VENCIDO" : "") + (CmsCifra.PermiteCifrar(c) ? "" : " · não permite cifrar"),
        };
        if (vencido) t.Foreground = Brushes.IndianRed;
        return t;
    }

    X509Certificate2? Selecionado => (lista.SelectedItem as ListBoxItem)?.Tag as X509Certificate2;

    void MostrarDetalhes(ListBoxItem? item)
    {
        if (item?.Tag is not X509Certificate2 c) return;
        detalhes.Children.Clear();
        void Linha(string rotulo, string valor)
        {
            detalhes.Children.Add(new TextBlock { Text = rotulo, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) });
            detalhes.Children.Add(new SelectableTextBlock { Text = valor, TextWrapping = TextWrapping.Wrap });
        }
        var ku = c.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        var eku = c.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        var agora = DateTime.Now;
        detalhes.Children.Add(new TextBlock { Text = CmsCifra.Nome(c), FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        Linha("Situação", c.NotBefore > agora ? "Ainda não é válido" : c.NotAfter < agora ? "VENCIDO" : "Dentro da validade (a revogação não é verificada)");
        Linha("Titular", c.Subject);
        Linha("Emissor", c.Issuer);
        Linha("Validade", $"de {c.NotBefore:dd/MM/yyyy HH:mm} a {c.NotAfter:dd/MM/yyyy HH:mm}");
        Linha("Número de série", c.SerialNumber);
        Linha("Chave", $"RSA {c.GetRSAPublicKey()?.KeySize} bits");
        Linha("Uso da chave", ku is null ? "(não informado)" : ku.KeyUsages.ToString());
        Linha("Uso estendido", eku is null ? "(não informado)" : string.Join(", ", eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.FriendlyName ?? o.Value)));
        Linha("Pode receber arquivos cifrados?", CmsCifra.PermiteCifrar(c) ? "Sim (keyEncipherment)" : "Não: falta keyEncipherment no uso da chave");
        Linha("Impressão digital SHA-256", Convert.ToHexString(c.GetCertHash(HashAlgorithmName.SHA256)));
        Linha("Impressão digital SHA-1", c.Thumbprint);
    }

    async Task AdicionarMeu()
    {
        try
        {
            var meus = await Task.Run(amb.Credencial.CertificadosPublicos);
            if (meus.Count == 0)
            {
                mensagem.Text = amb.Cfg.Modo == ModoCredencial.Pfx
                    ? "No modo .pfx, conecte a credencial em Decifrar Arquivos ou importe o seu .cer."
                    : "Nenhum certificado encontrado na credencial. Confira o token e o driver em Preferências.";
                return;
            }
            var novos = meus.Count(amb.Agenda.Adicionar);
            mensagem.Text = novos > 0 ? $"{novos} certificado(s) seu(s) adicionado(s) aos contatos." : "Os seus certificados já estavam nos contatos.";
        }
        catch (Exception e) when (e is CryptographicException or IOException or Net.Pkcs11Interop.Common.Pkcs11Exception
                                      or Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException or BadImageFormatException)
        {
            mensagem.Text = "Não consegui ler o token: " + e.Message;
        }
        AoMostrar();
    }

    async Task Importar()
    {
        var arquivos = await Ui.AbrirArquivos(this, "Importar certificados", true,
            ("Certificados (*.cer, *.crt, *.pem, *.der)", new[] { "*.cer", "*.crt", "*.pem", "*.der" }), ("Todos os arquivos", new[] { "*" }));
        var novos = 0;
        var erros = new List<string>();
        foreach (var a in arquivos)
        {
            try { if (amb.Agenda.Adicionar(CmsCifra.LerCertificado(a))) novos++; }
            catch (CryptographicException e) { erros.Add($"{Path.GetFileName(a)}: {e.Message}"); }
        }
        mensagem.Text = $"{novos} contato(s) importado(s)." + (erros.Count > 0 ? " Falhas: " + string.Join("; ", erros) : "");
        AoMostrar();
    }

    async Task DoRepositorio()
    {
        var escolhidos = await new JanelaEscolherCertificado(amb.Cfg).ShowDialog<List<X509Certificate2>?>(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
        if (escolhidos is null) return;
        mensagem.Text = $"{escolhidos.Count(amb.Agenda.Adicionar)} contato(s) adicionado(s).";
        AoMostrar();
    }

    async Task Exportar()
    {
        if (Selecionado is not { } c) { mensagem.Text = "Selecione o contato (por exemplo, você mesmo) para exportar o .cer."; return; }
        var destino = await Ui.SalvarArquivo(this, "Exportar certificado público", CmsCifra.Nome(c) + ".cer", "cer");
        if (destino is null) return;
        await File.WriteAllBytesAsync(destino, c.Export(X509ContentType.Cert));
        mensagem.Text = "Certificado público exportado em " + destino + ". Envie esse arquivo a quem vai cifrar para você.";
    }

    async Task Remover()
    {
        if (Selecionado is not { } c) return;
        if (!await Ui.Confirmar(this, $"Remover o contato \"{CmsCifra.Nome(c)}\"?")) return;
        amb.Agenda.Remover(c);
        AoMostrar();
    }

    async Task AbrirPasta()
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is { } l)
            await l.LaunchDirectoryInfoAsync(new DirectoryInfo(amb.Agenda.Pasta));
        mensagem.Text = "Pasta dos contatos: " + amb.Agenda.Pasta;
    }
}
