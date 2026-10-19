using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>Janela com o driver do token no topo e as abas Cifrar e Decifrar.</summary>
sealed class JanelaPrincipal : Form
{
    static readonly string ArquivoConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CifraToken", "driver-pkcs11.txt");

    readonly TextBox driver = new() { Dock = DockStyle.Fill };

    readonly TextBox arquivoCifrar = new() { Dock = DockStyle.Fill };
    readonly ListView destinatarios = Listas.Certificados();
    readonly CheckBox oaep = new() { Text = "Usar RSA-OAEP (só se todos os tokens dos destinatários suportarem)", AutoSize = true };

    readonly TextBox arquivoDecifrar = new() { Dock = DockStyle.Fill };
    readonly TextBox infoDestinatarios = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly RadioButton usarDriver = new() { Text = "Token pelo driver PKCS#11 indicado no topo (recomendado para o Dexon)", AutoSize = true, Checked = true };
    readonly RadioButton usarWindows = new() { Text = "Token ou certificado publicado no Windows (o middleware pede o PIN)", AutoSize = true };
    readonly RadioButton usarPfx = new() { Text = "Certificado A1 em arquivo (.pfx / .p12):", AutoSize = true };
    readonly TextBox arquivoPfx = new() { Width = 360, Enabled = false };

    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public JanelaPrincipal(string? arquivoInicial)
    {
        Text = "CifraToken - cifrar arquivos com certificado digital";
        Size = new Size(920, 680);
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var abas = new TabControl { Dock = DockStyle.Fill };
        abas.TabPages.Add(AbaCifrar());
        abas.TabPages.Add(AbaDecifrar());
        var barra = new StatusStrip();
        barra.Items.Add(status);
        Controls.Add(abas);
        Controls.Add(LinhaDriver());
        Controls.Add(barra);

        driver.Text = LerConfig() ?? TokenPkcs11.PrimeiraBibliotecaInstalada() ?? "";
        Status(driver.Text.Length > 0
            ? "Driver do token: " + driver.Text
            : "Driver PKCS#11 do token não encontrado. Instale o middleware DXSafe ou indique a DLL no topo.");

        if (arquivoInicial is not null && File.Exists(arquivoInicial))
        {
            if (arquivoInicial.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase))
            {
                abas.SelectedIndex = 1;
                DefinirArquivoDecifrar(arquivoInicial);
            }
            else
                arquivoCifrar.Text = arquivoInicial;
        }
    }

    Control LinhaDriver()
    {
        var linha = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.Controls.Add(new Label { Text = "Driver do token (PKCS#11):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        linha.Controls.Add(driver, 1, 0);
        linha.Controls.Add(Listas.Botao("Escolher...", (_, _) =>
        {
            using var d = new OpenFileDialog
            {
                Filter = "Biblioteca PKCS#11 (*.dll)|*.dll",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
                FileName = "DXSafePKCS11.dll",
            };
            if (d.ShowDialog(this) == DialogResult.OK) { driver.Text = d.FileName; GravarConfig(d.FileName); }
        }), 2, 0);
        linha.Controls.Add(Listas.Botao("Testar token", (_, _) => TestarToken()), 3, 0);
        return linha;
    }

    TabPage AbaCifrar()
    {
        var aba = new TabPage("Cifrar") { Padding = new Padding(10) };
        var grade = Listas.Grade(5);

        grade.Controls.Add(Listas.Rotulo("1. Arquivo a cifrar (ou arraste para esta janela):"), 0, 0);
        grade.Controls.Add(LinhaArquivo(arquivoCifrar, "Todos os arquivos|*.*", null), 0, 1);

        grade.Controls.Add(Listas.Rotulo("2. Destinatários (quem poderá decifrar). Inclua você mesmo e, se houver, o certificado de contingência do setor:"), 0, 2);
        var painelLista = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        painelLista.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        painelLista.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var botoes = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        botoes.Controls.Add(Listas.Botao("Do token / Windows...", (_, _) => AdicionarDoRepositorio()));
        botoes.Controls.Add(Listas.Botao("De arquivo .cer...", (_, _) => AdicionarDeArquivo()));
        botoes.Controls.Add(Listas.Botao("Remover", (_, _) =>
        {
            foreach (ListViewItem item in destinatarios.SelectedItems) destinatarios.Items.Remove(item);
        }));
        painelLista.Controls.Add(destinatarios, 0, 0);
        painelLista.Controls.Add(botoes, 1, 0);
        grade.Controls.Add(painelLista, 0, 3);
        grade.RowStyles[3] = new RowStyle(SizeType.Percent, 100);

        var rodape = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var cifrar = Listas.Botao("3. Cifrar e salvar .p7m...", (_, _) => Cifrar());
        cifrar.Font = new Font(cifrar.Font, FontStyle.Bold);
        rodape.Controls.Add(cifrar);
        rodape.Controls.Add(oaep);
        grade.Controls.Add(rodape, 0, 4);

        aba.Controls.Add(grade);
        Listas.AceitarArrastar(aba, f => arquivoCifrar.Text = f);
        return aba;
    }

    TabPage AbaDecifrar()
    {
        var aba = new TabPage("Decifrar") { Padding = new Padding(10) };
        var grade = Listas.Grade(6);

        grade.Controls.Add(Listas.Rotulo("1. Arquivo cifrado (.p7m), ou arraste para esta janela:"), 0, 0);
        grade.Controls.Add(LinhaArquivo(arquivoDecifrar, "Arquivo cifrado (*.p7m)|*.p7m|Todos os arquivos|*.*", DefinirArquivoDecifrar), 0, 1);
        grade.Controls.Add(Listas.Rotulo("Este arquivo foi cifrado para:"), 0, 2);
        grade.Controls.Add(infoDestinatarios, 0, 3);
        grade.RowStyles[3] = new RowStyle(SizeType.Percent, 100);

        var origem = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        origem.Controls.Add(Listas.Rotulo("2. Onde está a sua chave privada:"));
        origem.Controls.Add(usarDriver);
        origem.Controls.Add(usarWindows);
        var linhaPfx = new FlowLayoutPanel { AutoSize = true };
        var escolherPfx = Listas.Botao("Escolher...", (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "Certificado A1 (*.pfx;*.p12)|*.pfx;*.p12" };
            if (d.ShowDialog(this) == DialogResult.OK) arquivoPfx.Text = d.FileName;
        });
        escolherPfx.Enabled = false;
        usarPfx.CheckedChanged += (_, _) => { arquivoPfx.Enabled = escolherPfx.Enabled = usarPfx.Checked; };
        linhaPfx.Controls.Add(usarPfx);
        linhaPfx.Controls.Add(arquivoPfx);
        linhaPfx.Controls.Add(escolherPfx);
        origem.Controls.Add(linhaPfx);
        grade.Controls.Add(origem, 0, 4);

        var decifrar = Listas.Botao("3. Decifrar e salvar...", (_, _) => Decifrar());
        decifrar.Font = new Font(decifrar.Font, FontStyle.Bold);
        grade.Controls.Add(decifrar, 0, 5);

        aba.Controls.Add(grade);
        Listas.AceitarArrastar(aba, DefinirArquivoDecifrar);
        return aba;
    }

    Control LinhaArquivo(TextBox caixa, string filtro, Action<string>? aoEscolher)
    {
        var linha = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.Controls.Add(caixa, 0, 0);
        linha.Controls.Add(Listas.Botao("Escolher...", (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = filtro };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (aoEscolher is null) caixa.Text = d.FileName; else aoEscolher(d.FileName);
        }), 1, 0);
        return linha;
    }

    string? Driver() => string.IsNullOrWhiteSpace(driver.Text) ? null : driver.Text.Trim();

    void TestarToken()
    {
        if (Driver() is not { } caminho) { Erro("Indique a DLL do driver PKCS#11 (no Dexon, DXSafePKCS11.dll em C:\\Windows\\System32)."); return; }
        Executar(() =>
        {
            using var token = new TokenPkcs11(caminho);
            var certs = token.Certificados();
            GravarConfig(caminho);
            var texto = certs.Count == 0
                ? "O driver carregou, mas nenhum certificado foi encontrado. O token está conectado?"
                : string.Join("\n", certs.Select(c =>
                    $"• {CmsCifra.Nome(c.Certificado)} (token {c.Token}, válido até {c.Certificado.NotAfter:dd/MM/yyyy})" +
                    (CmsCifra.PermiteCifrar(c.Certificado) ? "" : " - não permite cifrar")));
            Status($"Token: {certs.Count} certificado(s) encontrado(s).");
            MessageBox.Show(this, texto, "Certificados no token", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    void AdicionarDoRepositorio()
    {
        using var janela = new EscolherCertificado(Driver);
        if (janela.ShowDialog(this) != DialogResult.OK) return;
        foreach (var (cert, origem) in janela.Escolhidos) AdicionarDestinatario(cert, origem);
    }

    void AdicionarDeArquivo()
    {
        using var d = new OpenFileDialog
        {
            Filter = "Certificado (*.cer;*.crt;*.pem;*.der)|*.cer;*.crt;*.pem;*.der|Todos os arquivos|*.*",
            Multiselect = true,
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        foreach (var caminho in d.FileNames)
        {
            try { AdicionarDestinatario(CmsCifra.LerCertificado(caminho), "Arquivo " + Path.GetFileName(caminho)); }
            catch (CryptographicException e) { Erro($"Não consegui ler {Path.GetFileName(caminho)}: {e.Message}"); }
        }
    }

    void AdicionarDestinatario(X509Certificate2 cert, string origem)
    {
        if (destinatarios.Items.Cast<ListViewItem>().Any(i => ((X509Certificate2)i.Tag!).Thumbprint == cert.Thumbprint))
            return;
        if (!CmsCifra.PermiteCifrar(cert) && MessageBox.Show(this,
                $"O certificado \"{CmsCifra.Nome(cert)}\" não tem o uso \"cifragem de chave\" (keyEncipherment). " +
                "O token pode recusar a decifração. Adicionar mesmo assim?",
                "Certificado sem uso para cifrar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        if (cert.NotAfter < DateTime.Now)
            MessageBox.Show(this, $"O certificado \"{CmsCifra.Nome(cert)}\" venceu em {cert.NotAfter:dd/MM/yyyy}. " +
                "Ainda é possível cifrar, mas confirme que o destinatário guarda essa chave.",
                "Certificado vencido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        destinatarios.Items.Add(Listas.Item(cert, origem));
    }

    void Cifrar()
    {
        if (!File.Exists(arquivoCifrar.Text)) { Erro("Escolha o arquivo a cifrar."); return; }
        var itens = destinatarios.Items.Cast<ListViewItem>().ToList();
        if (itens.Count == 0) { Erro("Adicione pelo menos um destinatário."); return; }
        var certs = itens.Select(i => (X509Certificate2)i.Tag!).ToList();
        var temMeu = itens.Any(i => i.SubItems[3].Text.StartsWith("Token") || ((X509Certificate2)i.Tag!).HasPrivateKey);
        if (!temMeu && MessageBox.Show(this,
                "Nenhum destinatário veio do seu token ou tem chave neste computador. " +
                "Se você não estiver na lista, não conseguirá decifrar o arquivo. Continuar?",
                "Confirmar destinatários", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        using var salvar = new SaveFileDialog
        {
            FileName = Path.GetFileName(arquivoCifrar.Text) + ".p7m",
            InitialDirectory = Path.GetDirectoryName(arquivoCifrar.Text),
            Filter = "Arquivo cifrado (*.p7m)|*.p7m",
        };
        if (salvar.ShowDialog(this) != DialogResult.OK) return;
        Executar(() =>
        {
            var p7m = CmsCifra.Cifrar(File.ReadAllBytes(arquivoCifrar.Text), certs,
                oaep.Checked ? PreenchimentoRsa.OaepSha1 : PreenchimentoRsa.Pkcs1);
            File.WriteAllBytes(salvar.FileName, p7m);
            Status($"Cifrado para {certs.Count} destinatário(s): {salvar.FileName}");
            MessageBox.Show(this, $"Arquivo cifrado salvo em:\n{salvar.FileName}\n\nO arquivo original não foi apagado.",
                "Cifrado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    void DefinirArquivoDecifrar(string caminho)
    {
        arquivoDecifrar.Text = caminho;
        try
        {
            var meus = Repositorio.Pessoais().Where(c => c.HasPrivateKey).ToList();
            infoDestinatarios.Lines = CmsCifra.Destinatarios(File.ReadAllBytes(caminho)).Select(d =>
            {
                var meu = meus.FirstOrDefault(d.Corresponde);
                return meu is null ? d.ToString() : $"{d}   <- {CmsCifra.Nome(meu)} (publicado neste Windows)";
            }).ToArray();
        }
        catch (Exception e) when (e is CryptographicException or IOException or FormatException)
        {
            infoDestinatarios.Text = "Não é um arquivo CMS cifrado (.p7m) válido: " + e.Message;
        }
    }

    void Decifrar()
    {
        if (!File.Exists(arquivoDecifrar.Text)) { Erro("Escolha o arquivo .p7m."); return; }
        string? senhaPfx = null;
        if (usarPfx.Checked)
        {
            if (!File.Exists(arquivoPfx.Text)) { Erro("Escolha o arquivo .pfx."); return; }
            senhaPfx = PedirSenha.Mostrar(this, "Senha do certificado A1 (.pfx):");
            if (senhaPfx is null) return;
        }
        if (usarDriver.Checked && Driver() is null)
        {
            Erro("Indique a DLL do driver PKCS#11 no topo da janela, ou escolha outra opção.");
            return;
        }

        var nome = Path.GetFileName(arquivoDecifrar.Text);
        if (nome.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase)) nome = nome[..^4];
        using var salvar = new SaveFileDialog
        {
            FileName = nome,
            InitialDirectory = Path.GetDirectoryName(arquivoDecifrar.Text),
            Filter = "Todos os arquivos|*.*",
        };
        if (salvar.ShowDialog(this) != DialogResult.OK) return;

        Executar(() =>
        {
            var p7m = File.ReadAllBytes(arquivoDecifrar.Text);
            byte[]? claro;
            if (usarDriver.Checked) claro = DecifrarPeloDriver(p7m);
            else if (usarPfx.Checked) claro = DecifrarComPfx(p7m, senhaPfx!);
            else claro = CmsCifra.Decifrar(p7m);
            if (claro is null) return;

            File.WriteAllBytes(salvar.FileName, claro);
            Status("Decifrado: " + salvar.FileName);
            MessageBox.Show(this, "Arquivo decifrado salvo em:\n" + salvar.FileName, "Decifrado",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    byte[]? DecifrarPeloDriver(byte[] p7m)
    {
        using var token = new TokenPkcs11(Driver()!);
        var destinos = CmsCifra.Destinatarios(p7m);
        var certs = token.Certificados();
        if (certs.Count == 0)
            throw new CryptographicException("Nenhum certificado encontrado no token. Ele está conectado?");
        var alvo = certs.FirstOrDefault(c => destinos.Any(d => d.Corresponde(c.Certificado)))
            ?? throw new CryptographicException(
                "Nenhum certificado do token conectado é destinatário deste arquivo. " +
                "Se o seu certificado foi renovado, o arquivo pode ter sido cifrado para o anterior.");
        var pin = PedirSenha.Mostrar(this, $"PIN do token \"{alvo.Token}\" para {CmsCifra.Nome(alvo.Certificado)}:",
            token.AvisoPin(alvo));
        if (pin is null) return null;
        using var chave = token.ChavePrivada(alvo, pin);
        return CmsCifra.Decifrar(p7m, alvo.Certificado, chave);
    }

    byte[] DecifrarComPfx(byte[] p7m, string senha)
    {
        var chaves = new X509Certificate2Collection();
        try
        {
            chaves.Import(arquivoPfx.Text, senha, X509KeyStorageFlags.UserKeySet);
            return CmsCifra.Decifrar(p7m, chaves);
        }
        finally
        {
            foreach (var c in chaves) c.Dispose();
        }
    }

    void Executar(Action acao)
    {
        UseWaitCursor = true;
        try { acao(); }
        catch (CryptographicException e)
        {
            Erro("Falha na operação criptográfica: " + e.Message);
        }
        catch (Net.Pkcs11Interop.Common.Pkcs11Exception e)
        {
            Erro(TokenPkcs11.Mensagem(e.RV));
        }
        catch (Exception e) when (e is Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException or BadImageFormatException)
        {
            Erro("Não consegui carregar o driver do token: " + e.Message +
                 "\n\nConfira se o middleware DXSafe está instalado e se a DLL indicada existe.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Erro(e.Message);
        }
        finally { UseWaitCursor = false; }
    }

    static string? LerConfig()
    {
        try { return File.Exists(ArquivoConfig) ? File.ReadAllText(ArquivoConfig).Trim() : null; }
        catch (IOException) { return null; }
    }

    static void GravarConfig(string caminho)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArquivoConfig)!);
            File.WriteAllText(ArquivoConfig, caminho);
        }
        catch (IOException) { }
    }

    void Status(string texto) => status.Text = texto;

    void Erro(string texto)
    {
        Status(texto.Split('\n')[0]);
        MessageBox.Show(this, texto, "CifraToken", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
