using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>Cifrar Arquivos: para destinatários (contatos) ou com frase-senha, alternando pelas setas.</summary>
sealed class PaginaCifrar : PaginaArquivos, IAoMostrar
{
    readonly Action irParaContatos;
    bool modoSenha;
    readonly TextBlock tituloModo = new() { Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly ContentControl painelModo = new();

    // destinatários
    readonly TextBox busca = new() { Watermark = "Buscar contato", Width = 220 };
    readonly StackPanel listaContatos = new() { Spacing = 2 };
    readonly HashSet<string> marcados = new();
    readonly CheckBox oaep = new() { Content = "Usar RSA-OAEP (só se o token de todos os destinatários suportar)" };
    readonly Control painelDestinatarios;

    // senha
    readonly TextBox senha = new() { PasswordChar = '•', Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox confirma = new() { PasswordChar = '•', Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ProgressBar forca = new() { Minimum = 0, Maximum = 4, Width = 340, Height = 8, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock textoForca = new() { FontSize = 11 };
    readonly Control painelSenha;

    protected override (string Nome, string[] Padroes)[] Filtros => new[] { ("Todos os arquivos", new[] { "*" }) };

    public PaginaCifrar(Ambiente amb, Action irParaContatos) : base(amb)
    {
        this.irParaContatos = irParaContatos;
        painelDestinatarios = MontarDestinatarios();
        painelSenha = MontarSenha();

        var faixa = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 34,
            Background = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
        };
        var anterior = Ui.Botao(Icones.Anterior, "Alternar modo", Alternar);
        var proximo = Ui.Botao(Icones.Proximo, "Alternar modo", Alternar);
        anterior.Foreground = proximo.Foreground = Brushes.White;
        faixa.Children.Add(anterior);
        Grid.SetColumn(tituloModo, 1);
        faixa.Children.Add(tituloModo);
        Grid.SetColumn(proximo, 2);
        faixa.Children.Add(proximo);

        Direita.Content = new StackPanel { Spacing = 8, Children = { faixa, painelModo } };
        amb.Mudou += () => { oaep.IsChecked = Amb.Cfg.UsarOaep; CarregarContatos(); };
        oaep.IsChecked = amb.Cfg.UsarOaep;
        AtualizarModo();
    }

    public void AoMostrar() => CarregarContatos();

    void Alternar()
    {
        modoSenha = !modoSenha;
        AtualizarModo();
    }

    void AtualizarModo()
    {
        tituloModo.Text = modoSenha ? "Cifrar com Senha" : "Cifrar para Destinatários";
        painelModo.Content = modoSenha ? painelSenha : painelDestinatarios;
    }

    Control MontarDestinatarios()
    {
        busca.TextChanged += (_, _) => CarregarContatos();
        var topo = new DockPanel { Children = { busca } };
        DockPanel.SetDock(busca, Dock.Right);
        topo.Children.Add(new TextBlock { Text = "Destinatários:", VerticalAlignment = VerticalAlignment.Center });

        var avancadas = new Expander
        {
            Header = "Mostrar opções avançadas", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    oaep,
                    Ui.BotaoSimples("Salvar como padrão", () => { var c = Amb.Cfg.Copia(); c.UsarOaep = oaep.IsChecked == true; Amb.TrocarConfiguracao(c); }),
                },
            },
        };
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                topo,
                new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)), BorderThickness = new Thickness(1),
                    Padding = new Thickness(6), MinHeight = 200, Child = listaContatos,
                },
                new TextBlock
                {
                    Text = "Inclua você mesmo entre os destinatários, senão não conseguirá decifrar o arquivo depois.",
                    Opacity = 0.75, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                },
                avancadas,
            },
        };
    }

    void CarregarContatos()
    {
        listaContatos.Children.Clear();
        var todos = Amb.Agenda.Todos();
        if (todos.Count == 0)
        {
            listaContatos.Children.Add(new TextBlock
            {
                Text = "Nenhum contato cadastrado. Cadastre os certificados dos destinatários (e o seu) em Contatos e Certificados.",
                TextWrapping = TextWrapping.Wrap,
            });
            listaContatos.Children.Add(Ui.BotaoSimples("Ir para Contatos e Certificados", irParaContatos));
            return;
        }
        var filtro = busca.Text?.Trim() ?? "";
        foreach (var cert in todos.Where(c => filtro.Length == 0 || CmsCifra.Nome(c).Contains(filtro, StringComparison.CurrentCultureIgnoreCase)))
        {
            var vencido = cert.NotAfter < DateTime.Now;
            var caixa = new CheckBox
            {
                Content = $"{CmsCifra.Nome(cert)}   (válido até {cert.NotAfter:dd/MM/yyyy}{(vencido ? ", VENCIDO" : "")}" +
                          $"{(CmsCifra.PermiteCifrar(cert) ? "" : ", não permite cifrar")})",
                IsChecked = marcados.Contains(cert.Thumbprint), Tag = cert,
                IsEnabled = CmsCifra.PermiteCifrar(cert),
            };
            caixa.IsCheckedChanged += (_, _) =>
            {
                if (caixa.IsChecked == true) marcados.Add(cert.Thumbprint); else marcados.Remove(cert.Thumbprint);
            };
            listaContatos.Children.Add(caixa);
        }
    }

    Control MontarSenha()
    {
        var mostrar = new CheckBox { Content = "Mostrar senha" };
        mostrar.IsCheckedChanged += (_, _) => senha.PasswordChar = confirma.PasswordChar = mostrar.IsChecked == true ? '\0' : '•';
        senha.TextChanged += (_, _) =>
        {
            var f = SenhaOpenSsl.Forca(senha.Text ?? "");
            forca.Value = f;
            forca.Foreground = f >= 3 ? Brushes.SeaGreen : f == 2 ? Brushes.Orange : Brushes.IndianRed;
            textoForca.Text = new[] { "muito fraca", "fraca", "média", "boa", "forte" }[f];
        };
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Ui.Rotulo("Frase-senha:"), senha,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { forca, textoForca } },
                Ui.Rotulo("Confirme a frase-senha:"), confirma, mostrar,
                new TextBlock
                {
                    Margin = new Thickness(0, 10, 0, 0), Opacity = 0.75, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Text = "Use pelo menos 14 caracteres com maiúsculas, minúsculas, números e símbolos. " +
                           "O arquivo .enc usa AES-256 com PBKDF2-SHA512 (1.000.000 de iterações) e abre no Quimera e no openssl:\n" +
                           "openssl enc -aes-256-cbc -d -in ARQUIVO.enc -out ARQUIVO -pbkdf2 -iter 1000000 -md sha512",
                },
            },
        };
    }

    protected override void ArquivosMudaram()
    {
        if (Arquivos.Count > 0) Informar($"{Arquivos.Count} arquivo(s) para cifrar. Os originais não são apagados.");
    }

    protected override void Limpar()
    {
        base.Limpar();
        senha.Text = confirma.Text = "";
    }

    protected override async Task Executar()
    {
        if (Arquivos.Count == 0) { Informar("Adicione os arquivos a cifrar com o botão + ou arrastando-os.", erro: true); return; }

        Func<byte[], byte[]> cifra;
        string extensao;
        if (modoSenha)
        {
            var s = senha.Text ?? "";
            if (SenhaOpenSsl.Forca(s) < 3) { Informar("A frase-senha está fraca. Use pelo menos 14 caracteres variados.", erro: true); return; }
            if (s != confirma.Text) { Informar("A confirmação não confere com a frase-senha.", erro: true); return; }
            cifra = d => SenhaOpenSsl.Cifrar(d, s);
            extensao = ".enc";
        }
        else
        {
            var certs = Amb.Agenda.Todos().Where(c => marcados.Contains(c.Thumbprint)).ToList();
            if (certs.Count == 0) { Informar("Marque pelo menos um destinatário.", erro: true); return; }
            if (!await IncluiOUsuario(certs) && !await Ui.Confirmar(this,
                    "Nenhum dos destinatários marcados é o certificado da sua credencial. " +
                    "Você não conseguirá decifrar estes arquivos depois. Continuar mesmo assim?"))
                return;
            var preenchimento = oaep.IsChecked == true ? PreenchimentoRsa.OaepSha1 : PreenchimentoRsa.Pkcs1;
            cifra = d => CmsCifra.Cifrar(d, certs, preenchimento);
            extensao = ".p7m";
        }

        int feitos = 0, pulados = 0;
        var erros = new List<string>();
        foreach (var arquivo in Arquivos.ToList())
        {
            var destino = arquivo + extensao;
            var pode = await PodeGravar(destino);
            if (pode is null) break;
            if (pode == false) { pulados++; continue; }
            try
            {
                var dados = await File.ReadAllBytesAsync(arquivo);
                var saida = await Task.Run(() => cifra(dados));
                await File.WriteAllBytesAsync(destino, saida);
                feitos++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or OutOfMemoryException)
            {
                erros.Add($"{Path.GetFileName(arquivo)}: {e.Message}");
            }
        }
        var resumo = $"Foram cifrados {feitos} arquivo(s)" + (pulados > 0 ? $", {pulados} pulado(s)" : "") +
                     $". Cada um foi salvo ao lado do original com a extensão {extensao}.";
        Informar(erros.Count == 0 ? resumo : resumo + "\nFalhas:\n" + string.Join("\n", erros), erro: erros.Count > 0);
        if (modoSenha) senha.Text = confirma.Text = "";
    }

    async Task<bool> IncluiOUsuario(List<X509Certificate2> certs)
    {
        try
        {
            var meus = await Task.Run(Amb.Credencial.CertificadosPublicos);
            return meus.Count == 0 || certs.Any(c => meus.Any(m => m.Thumbprint == c.Thumbprint));
        }
        catch (Exception) { return true; } // sem acesso ao token agora: não bloqueia
    }
}
