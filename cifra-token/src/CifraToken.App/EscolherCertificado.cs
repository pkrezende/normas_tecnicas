using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>
/// Lista os certificados do token (lidos pelo driver PKCS#11) e os do repositório do Windows
/// ("Pessoal" e "Outras Pessoas") para escolher destinatários.
/// </summary>
sealed class EscolherCertificado : Form
{
    readonly ListView lista = Listas.Certificados();
    readonly Func<string?> driver;
    readonly Label aviso = new() { AutoSize = true, ForeColor = Color.DarkRed, MaximumSize = new Size(820, 0) };

    public List<(X509Certificate2 Cert, string Origem)> Escolhidos { get; } = new();

    public EscolherCertificado(Func<string?> driver)
    {
        this.driver = driver;
        Text = "Escolher certificados de destinatários";
        Size = new Size(860, 420);
        StartPosition = FormStartPosition.CenterParent;

        var grade = Listas.Grade(4);
        grade.Padding = new Padding(10);
        grade.Controls.Add(Listas.Rotulo("Conecte o token antes de abrir esta lista ou clique em Atualizar. Selecione um ou mais (Ctrl + clique)."), 0, 0);
        grade.Controls.Add(lista, 0, 1);
        grade.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        grade.Controls.Add(aviso, 0, 2);
        var botoes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Adicionar", DialogResult = DialogResult.OK, AutoSize = true };
        botoes.Controls.Add(new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true });
        botoes.Controls.Add(ok);
        botoes.Controls.Add(Listas.Botao("Atualizar", (_, _) => Carregar()));
        grade.Controls.Add(botoes, 0, 3);
        Controls.Add(grade);
        AcceptButton = ok;
        lista.DoubleClick += (_, _) => { DialogResult = DialogResult.OK; };

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            foreach (ListViewItem item in lista.SelectedItems)
                Escolhidos.Add(((X509Certificate2)item.Tag!, item.SubItems[3].Text));
        };
        Shown += (_, _) => Carregar();
    }

    void Carregar()
    {
        UseWaitCursor = true;
        lista.BeginUpdate();
        lista.Items.Clear();
        aviso.Text = "";
        var vistos = new HashSet<string>();

        var caminho = driver();
        if (!string.IsNullOrWhiteSpace(caminho))
        {
            try
            {
                using var token = new TokenPkcs11(caminho);
                foreach (var c in token.Certificados())
                    if (vistos.Add(c.Certificado.Thumbprint))
                        lista.Items.Add(Listas.Item(c.Certificado, $"Token {c.Token}"));
            }
            catch (Exception e)
            {
                aviso.Text = $"Não consegui ler o token pelo driver {Path.GetFileName(caminho)}: {e.Message}";
            }
        }
        foreach (var c in Repositorio.Pessoais())
            if (vistos.Add(c.Thumbprint))
                lista.Items.Add(Listas.Item(c, c.HasPrivateKey ? "Windows: Pessoal (com chave)" : "Windows: Pessoal"));
        foreach (var c in Repositorio.OutrasPessoas())
            if (vistos.Add(c.Thumbprint))
                lista.Items.Add(Listas.Item(c, "Windows: Outras Pessoas"));

        if (lista.Items.Count == 0 && aviso.Text.Length == 0)
            aviso.Text = "Nenhum certificado encontrado. Confira o driver do token no topo da janela principal.";
        lista.EndUpdate();
        UseWaitCursor = false;
    }
}
