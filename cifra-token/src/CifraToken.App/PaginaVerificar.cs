using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>
/// Verificar Assinaturas: para cada arquivo (ou o seu .p7s), confere a integridade, o signatário,
/// a política ICP-Brasil, o carimbo do tempo e a cadeia do certificado.
/// </summary>
sealed class PaginaVerificar : PaginaArquivos
{
    readonly StackPanel resultados = new() { Spacing = 8 };

    protected override (string Nome, string[] Padroes)[] Filtros => new[]
    {
        ("Assinaturas e PDFs (*.p7s, *.pdf)", new[] { "*.p7s", "*.pdf" }),
        ("Todos os arquivos", new[] { "*" }),
    };

    public PaginaVerificar(Ambiente amb) : base(amb)
    {
        Direita.Content = resultados;
        ArquivosMudaram();
    }

    /// <summary>Aceita o .p7s ou o original: "contrato.pdf.p7s" assina "contrato.pdf".</summary>
    static (string Original, string Assinatura) Par(string caminho) =>
        caminho.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase) ? (caminho[..^4], caminho) : (caminho, caminho + ".p7s");

    protected override void ArquivosMudaram()
    {
        resultados.Children.Clear();
        resultados.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Arquivos.Count == 0
                ? "Adicione os PDFs assinados, os arquivos assinados ou as assinaturas (.p7s). O original e o .p7s precisam estar na mesma pasta, com o mesmo nome (\"contrato.pdf\" e \"contrato.pdf.p7s\"). Um PDF sem .p7s ao lado tem as assinaturas internas conferidas. Depois clique em Ok."
                : $"{Arquivos.Count} arquivo(s). Clique em Ok para verificar.",
        });
    }

    protected override async Task Executar()
    {
        if (Arquivos.Count == 0) { Informar("Adicione os arquivos a verificar.", erro: true); return; }
        resultados.Children.Clear();
        int validas = 0, total = 0;
        foreach (var (original, assinatura) in Arquivos.Select(Par).Distinct().ToList())
        {
            // PDF sem .p7s ao lado: assinaturas dentro do próprio arquivo (PAdES)
            if (original.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !File.Exists(assinatura) && File.Exists(original))
            {
                List<VerificacaoPdf>? internas = null;
                string? problema = null;
                try
                {
                    var bytes = await File.ReadAllBytesAsync(original);
                    internas = await Task.Run(() => AssinaturaPdf.Verificar(bytes));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problema = e.Message; }
                if (internas is null || internas.Count == 0)
                {
                    total++;
                    resultados.Children.Add(Cartao(Path.GetFileName(original), null, problema ?? "O PDF não tem assinatura digital.", false));
                    continue;
                }
                foreach (var a in internas)
                {
                    total++;
                    var ok = a.Assinatura is { Integra: true } && a.Assinatura.CarimboValido != false;
                    if (ok) validas++;
                    var nome = Path.GetFileName(original) + (internas.Count > 1 ? $" (assinatura {a.Numero})" : "");
                    resultados.Children.Add(Cartao(nome, a.Assinatura, null, ok,
                        (a.CobreDocumentoInteiro ? "cobre o documento inteiro" : "ATENÇÃO: o PDF recebeu alterações depois desta assinatura") +
                        (a.Motivo is null ? "" : $"; motivo: {a.Motivo}")));
                }
                continue;
            }
            total++;
            Verificacao? v = null;
            string? falta = !File.Exists(original) ? $"Não encontrei o arquivo original \"{Path.GetFileName(original)}\" na mesma pasta."
                : !File.Exists(assinatura) ? $"Não encontrei a assinatura \"{Path.GetFileName(assinatura)}\" na mesma pasta." : null;
            if (falta is null)
            {
                try
                {
                    var conteudo = await File.ReadAllBytesAsync(original);
                    var p7s = await File.ReadAllBytesAsync(assinatura);
                    v = await Task.Run(() => AssinaturaCades.Verificar(conteudo, p7s));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
                {
                    falta = e.Message;
                }
            }
            var boa = v is { Integra: true } && v.CarimboValido != false;
            if (boa) validas++;
            resultados.Children.Add(Cartao(Path.GetFileName(original), v, falta, boa));
        }
        Informar(validas == total ? $"Todas as {total} assinatura(s) estão íntegras." : $"{validas} de {total} assinatura(s) íntegras.",
            erro: validas != total);
    }

    static Border Cartao(string nome, Verificacao? v, string? erro, bool boa, string? pdf = null)
    {
        var linhas = new StackPanel { Spacing = 2 };
        var titulo = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        titulo.Children.Add(Icones.Criar(boa ? Icones.Certo : Icones.Xis, 16));
        titulo.Children.Add(new TextBlock { Text = nome, FontWeight = FontWeight.SemiBold });
        linhas.Children.Add(titulo);
        void Linha(string rotulo, string? valor)
        {
            if (valor is null) return;
            linhas.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"{rotulo}: {valor}" });
        }
        if (v is null) Linha("Erro", erro);
        else
        {
            Linha("Situação", v.Integra ? "assinatura íntegra (o arquivo não foi alterado depois de assinado)" : "ASSINATURA INVÁLIDA" + (v.Erro is null ? "" : ": " + v.Erro));
            Linha("Signatário", v.Signatario);
            if (v.Certificado is { } c) Linha("Certificado", $"emitido por {c.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, true)}, válido de {c.NotBefore:dd/MM/yyyy} a {c.NotAfter:dd/MM/yyyy}");
            Linha("Política", v.Politica is { } oid ? PoliticaIcp.Descrever(oid) : "nenhuma (não é assinatura no padrão ICP-Brasil)");
            Linha("Carimbo do tempo", v.Carimbo is null ? "não tem" :
                $"{v.Carimbo.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss} (hora local) por {v.Act}, {(v.CarimboValido == true ? "íntegro" : "INVÁLIDO")}");
            Linha("PDF", pdf);
            Linha("Cadeia", v.CadeiaStatus);
        }
        return new Border
        {
            Padding = new Thickness(10), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(boa ? Color.FromArgb(35, 30, 160, 60) : Color.FromArgb(45, 220, 50, 50)),
            Child = linhas,
        };
    }
}
