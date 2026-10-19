using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>Pequenos construtores de controles usados pelas janelas.</summary>
static class Listas
{
    public static ListView Certificados() => Certificados(multipla: true);

    public static ListView Certificados(bool multipla)
    {
        var lista = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = multipla,
            HideSelection = false,
        };
        lista.Columns.Add("Nome", 260);
        lista.Columns.Add("Emissor", 200);
        lista.Columns.Add("Válido até", 90);
        lista.Columns.Add("Onde está", 170);
        lista.Columns.Add("Cifra?", 60);
        return lista;
    }

    public static ListViewItem Item(X509Certificate2 cert, string origem)
    {
        var item = new ListViewItem(new[]
        {
            CmsCifra.Nome(cert),
            cert.GetNameInfo(X509NameType.SimpleName, true),
            cert.NotAfter.ToString("dd/MM/yyyy"),
            origem,
            CmsCifra.PermiteCifrar(cert) ? "sim" : "não",
        }) { Tag = cert, ToolTipText = cert.Subject };
        if (cert.NotAfter < DateTime.Now || !CmsCifra.PermiteCifrar(cert)) item.ForeColor = Color.Gray;
        return item;
    }

    public static TableLayoutPanel Grade(int linhas)
    {
        var grade = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = linhas };
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < linhas; i++) grade.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return grade;
    }

    public static Label Rotulo(string texto) => new()
    {
        Text = texto, AutoSize = true, Padding = new Padding(0, 8, 0, 2), MaximumSize = new Size(820, 0),
    };

    public static Button Botao(string texto, EventHandler clique)
    {
        var b = new Button { Text = texto, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        b.Click += clique;
        return b;
    }

    public static void AceitarArrastar(Control alvo, Action<string> aoSoltar)
    {
        alvo.AllowDrop = true;
        alvo.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        alvo.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } arquivos) aoSoltar(arquivos[0]);
        };
    }
}
