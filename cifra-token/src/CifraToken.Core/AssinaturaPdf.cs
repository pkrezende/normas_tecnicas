using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace CifraToken.Core;

/// <summary>
/// Visível: com carimbo do tempo, carimbo redondo "CARIMBADO E ASSINADO"; sem carimbo, quadro de assinatura.
/// Nunca os dois juntos.
/// </summary>
public enum EstiloEstampa { Nenhuma, Visivel }

public enum PosicaoEstampa { InferiorDireito, InferiorEsquerdo, SuperiorDireito, SuperiorEsquerdo, Centro }

/// <summary>
/// Aparência da assinatura na página, com logo opcional (PNG ou JPEG). O desenho depende de haver carimbo do tempo.
/// <paramref name="Pagina"/> começa em 1; 0 é a última página.
/// </summary>
public sealed record Estampa(EstiloEstampa Estilo, int Pagina = 0, PosicaoEstampa Posicao = PosicaoEstampa.InferiorDireito,
    byte[]? Logo = null, string? Motivo = null, string? Local = null);

/// <summary>Resultado da verificação de uma assinatura dentro do PDF.</summary>
public sealed record VerificacaoPdf(int Numero, Verificacao Assinatura, bool CobreDocumentoInteiro, string? Motivo, string? Data);

/// <summary>
/// Assinatura PAdES (ETSI EN 319 142, SubFilter ETSI.CAdES.detached) com a Política de Assinatura PAdES da ICP-Brasil
/// (DOC-ICP-15.03 v9.1: AD-RB v1.3 e AD-RT v1.3). O CMS é o mesmo CAdES de <see cref="AssinaturaCades"/>, sem a hora do computador
/// (no PAdES ela vai em /M). A estampa é desenhada na página antes da assinatura, então também fica protegida por ela.
/// </summary>
public static class AssinaturaPdf
{
    /// <summary>Espaço reservado para o CMS (assinatura, certificados e carimbo do tempo).</summary>
    const int TamanhoReservado = 32 * 1024;
    const string MarcaByteRange = "[0 9999999999 9999999999 9999999999]";

    public static bool EhPdf(byte[] dados) => dados.Length > 5 && Encoding.ASCII.GetString(dados, 0, 5) == "%PDF-";

    public static bool JaAssinado(byte[] pdf) => RegexByteRange.IsMatch(Encoding.Latin1.GetString(pdf));

    public static async Task<byte[]> Assinar(byte[] pdf, X509Certificate2 certificado, RSA? chave, PoliticaIcp? politica,
        string? urlAct, HttpClient? http, Estampa estampa, IEnumerable<X509Certificate2>? cadeia = null,
        string? politicaAct = null, bool silencioso = true, CancellationToken ct = default)
    {
        if (!EhPdf(pdf)) throw new ArgumentException("O arquivo não é um PDF.");
        if (JaAssinado(pdf))
            throw new ArgumentException("Este PDF já tem assinatura digital. Assinar de novo apagaria a anterior; " +
                                        "use a assinatura destacada (.p7s) para assinar em conjunto.");
        if (politica?.ExigeCarimbo == true && string.IsNullOrWhiteSpace(urlAct))
            throw new ArgumentException("A política AD-RT exige carimbo do tempo: configure o endereço da ACT.");

        var agora = DateTime.UtcNow;
        var preparado = Preparar(pdf, certificado, estampa, agora, politica, urlAct);

        // ByteRange: tudo, menos o espaço entre < e > de /Contents
        var texto = Encoding.Latin1.GetString(preparado);
        var marca = texto.IndexOf(MarcaByteRange, StringComparison.Ordinal);
        var vazio = "<" + new string('0', TamanhoReservado * 2) + ">";
        var inicio = texto.IndexOf(vazio, StringComparison.Ordinal);
        if (marca < 0 || inicio < 0) throw new CryptographicException("Falha ao preparar o PDF para a assinatura.");
        var fim = inicio + vazio.Length;
        var faixa = $"[0 {inicio} {fim} {preparado.Length - fim}]".PadRight(MarcaByteRange.Length);
        Encoding.ASCII.GetBytes(faixa).CopyTo(preparado, marca);

        var assinado = new byte[inicio + preparado.Length - fim];
        Buffer.BlockCopy(preparado, 0, assinado, 0, inicio);
        Buffer.BlockCopy(preparado, fim, assinado, inicio, preparado.Length - fim);
        var cms = await AssinaturaCades.Assinar(assinado, certificado, chave, politica, urlAct, http, cadeia, politicaAct,
            silencioso, incluirHora: false, ct);
        if (cms.Length > TamanhoReservado) throw new CryptographicException("A assinatura ficou maior que o espaço reservado no PDF.");
        Encoding.ASCII.GetBytes(Convert.ToHexString(cms)).CopyTo(preparado, inicio + 1);
        return preparado;
    }

    /// <summary>Desenha a estampa e cria o campo de assinatura com os espaços reservados para ByteRange e Contents.</summary>
    static byte[] Preparar(byte[] pdf, X509Certificate2 cert, Estampa estampa, DateTime agora, PoliticaIcp? politica, string? urlAct)
    {
        Fontes.Registrar();
        PdfDocument doc;
        try { doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify); }
        catch (Exception e) when (e is not OutOfMemoryException) // o PDFsharp lança Exception genérica para PDF corrompido
        {
            throw new ArgumentException("Não consegui abrir o PDF (protegido por senha ou corrompido): " + e.Message, e);
        }
        using var _ = doc;
        if (doc.PageCount == 0) throw new ArgumentException("O PDF não tem páginas.");
        var indice = estampa.Pagina <= 0 || estampa.Pagina > doc.PageCount ? doc.PageCount - 1 : estampa.Pagina - 1;
        var pagina = doc.Pages[indice];

        var retangulo = new XRect();
        if (estampa.Estilo != EstiloEstampa.Nenhuma)
        {
            var redondo = !string.IsNullOrWhiteSpace(urlAct);
            var (l, a) = redondo ? (140.0, 140.0) : (270.0, 78.0);
            retangulo = Posicionar(pagina.Width.Point, pagina.Height.Point, l, a, estampa.Posicao);
            using var g = XGraphics.FromPdfPage(pagina, XGraphicsPdfPageOptions.Append);
            var dados = new DadosEstampa(CmsCifra.Nome(cert), agora, politica, urlAct, estampa.Logo);
            if (redondo) DesenharCarimbo(g, retangulo, dados);
            else DesenharQuadro(g, retangulo, dados);
        }

        var sig = new PdfDictionary(doc);
        sig.Elements.SetName("/Type", "/Sig");
        sig.Elements.SetName("/Filter", "/Adobe.PPKLite");
        sig.Elements.SetName("/SubFilter", "/ETSI.CAdES.detached");
        sig.Elements["/ByteRange"] = new PdfLiteral(MarcaByteRange);
        sig.Elements["/Contents"] = new PdfLiteral("<" + new string('0', TamanhoReservado * 2) + ">");
        sig.Elements["/M"] = new PdfDate(agora);
        sig.Elements.SetString("/Name", CmsCifra.Nome(cert));
        if (estampa.Motivo is { Length: > 0 } motivo) sig.Elements.SetString("/Reason", motivo);
        if (estampa.Local is { Length: > 0 } local) sig.Elements.SetString("/Location", local);
        sig.Elements["/Prop_Build"] = new PdfLiteral("<</App<</Name/CifraToken>>>>");
        doc.Internals.AddObject(sig);

        // Campo de assinatura (widget) sobre a estampa; a aparência é a própria página, então o /AP fica vazio
        var (x1, y1, x2, y2) = estampa.Estilo == EstiloEstampa.Nenhuma
            ? (0.0, 0.0, 0.0, 0.0)
            : (retangulo.X, pagina.Height.Point - retangulo.Bottom, retangulo.Right, pagina.Height.Point - retangulo.Y);
        var ap = new PdfDictionary(doc);
        ap.Elements.SetName("/Type", "/XObject");
        ap.Elements.SetName("/Subtype", "/Form");
        ap.Elements["/BBox"] = new PdfLiteral(Inv($"[0 0 {x2 - x1:0.##} {y2 - y1:0.##}]"));
        ap.CreateStream(Array.Empty<byte>());
        doc.Internals.AddObject(ap);
        var apDict = new PdfDictionary(doc);
        apDict.Elements.SetReference("/N", ap);

        var campo = new PdfDictionary(doc);
        campo.Elements.SetName("/FT", "/Sig");
        campo.Elements.SetName("/Type", "/Annot");
        campo.Elements.SetName("/Subtype", "/Widget");
        campo.Elements.SetString("/T", "Assinatura " + agora.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        campo.Elements["/Rect"] = new PdfLiteral(Inv($"[{x1:0.##} {y1:0.##} {x2:0.##} {y2:0.##}]"));
        campo.Elements.SetInteger("/F", 132); // imprimir + travado
        campo.Elements.SetReference("/V", sig);
        campo.Elements.SetReference("/P", pagina);
        campo.Elements["/AP"] = apDict;
        doc.Internals.AddObject(campo);

        var annots = pagina.Elements.GetArray("/Annots");
        if (annots is null)
        {
            annots = new PdfArray(doc);
            pagina.Elements["/Annots"] = annots;
        }
        annots.Elements.Add(campo.Reference!);

        var catalogo = doc.Internals.Catalog;
        var form = catalogo.Elements.GetDictionary("/AcroForm");
        if (form is null)
        {
            form = new PdfDictionary(doc);
            doc.Internals.AddObject(form);
            catalogo.Elements.SetReference("/AcroForm", form);
        }
        var campos = form.Elements.GetArray("/Fields");
        if (campos is null)
        {
            campos = new PdfArray(doc);
            form.Elements["/Fields"] = campos;
        }
        campos.Elements.Add(campo.Reference!);
        form.Elements.SetInteger("/SigFlags", 3);

        doc.Options.CompressContentStreams = true;
        using var saida = new MemoryStream();
        doc.Save(saida, false);
        return saida.ToArray();
    }

    static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    static XRect Posicionar(double largPagina, double altPagina, double l, double a, PosicaoEstampa p)
    {
        const double margem = 36; // meia polegada
        var x = p is PosicaoEstampa.InferiorEsquerdo or PosicaoEstampa.SuperiorEsquerdo ? margem
              : p == PosicaoEstampa.Centro ? (largPagina - l) / 2 : largPagina - margem - l;
        var y = p is PosicaoEstampa.SuperiorDireito or PosicaoEstampa.SuperiorEsquerdo ? margem
              : p == PosicaoEstampa.Centro ? (altPagina - a) / 2 : altPagina - margem - a;
        return new XRect(Math.Max(0, x), Math.Max(0, y), l, a);
    }

    sealed record DadosEstampa(string Signatario, DateTime Utc, PoliticaIcp? Politica, string? Act, byte[]? Logo)
    {
        public string Padrao => Politica is null ? "Assinatura digital · PAdES" : "ICP-Brasil · " + PoliticaIcp.Descrever(Politica.Oid).Split(" da ")[0];
        public string? Servidor => Act is not null && Uri.TryCreate(Act, UriKind.Absolute, out var u) ? u.Host : Act;
    }

    /// <summary>
    /// Carimbo redondo de borracha, levemente inclinado: "CARIMBADO E ASSINADO" no arco de cima, ICP-Brasil e o padrão
    /// no arco de baixo, e no miolo o logo, a data, a hora e o signatário.
    /// </summary>
    static void DesenharCarimbo(XGraphics g, XRect r, DadosEstampa d)
    {
        var tinta = XColor.FromArgb(225, 22, 45, 140);
        var pincel = new XSolidBrush(tinta);
        var centro = new XPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
        var raio = Math.Min(r.Width, r.Height) / 2 - 2;
        var estado = g.Save();
        g.RotateAtTransform(-8, centro);
        Circulo(g, new XPen(tinta, 2.6), centro, raio);
        Circulo(g, new XPen(tinta, 0.9), centro, raio - 5);
        Circulo(g, new XPen(tinta, 0.9), centro, raio - 22);

        var arco = raio - 13.5; // meio da faixa entre os anéis
        TextoNoArco(g, "CARIMBADO E ASSINADO", new XFont("Liberation Sans", 9, XFontStyleEx.Bold), pincel, centro, arco, emCima: true);
        TextoNoArco(g, d.Padrao.ToUpperInvariant().Replace("PADES", "PAdES"), new XFont("Liberation Sans", 6), pincel, centro, arco, emCima: false);
        foreach (var lado in new[] { -1, 1 }) // estrelinhas que separam os dois arcos
            g.DrawString("★", new XFont("Liberation Sans", 7), pincel, new XRect(centro.X + lado * arco - 5, centro.Y - 5, 10, 10), XStringFormats.Center);

        var miolo = raio - 24;
        var y = centro.Y - miolo + 4;
        if (DesenharLogo(g, d.Logo, new XRect(centro.X - 11, y, 22, 22))) y += 23;
        else y += 8;
        var largura = new XRect(centro.X - miolo, 0, miolo * 2, 0);
        Linha(g, $"{d.Utc:dd} {Meses[d.Utc.Month - 1]} {d.Utc:yyyy}", new XFont("Liberation Sans", 8.5, XFontStyleEx.Bold), pincel, largura, ref y, 10);
        Linha(g, $"{d.Utc:HH:mm} UTC", new XFont("Liberation Sans", 7.5, XFontStyleEx.Bold), pincel, largura, ref y, 9);
        Linha(g, Cortar(d.Signatario.Split(':')[0].ToUpperInvariant(), 22), new XFont("Liberation Sans", 5.5, XFontStyleEx.Bold), pincel, largura, ref y, 7);
        g.Restore(estado);
    }

    static void Circulo(XGraphics g, XPen caneta, XPoint c, double raio) =>
        g.DrawEllipse(caneta, c.X - raio, c.Y - raio, raio * 2, raio * 2);

    /// <summary>Escreve letra por letra ao longo do arco: em cima lendo da esquerda para a direita, embaixo também, de pé.</summary>
    static void TextoNoArco(XGraphics g, string texto, XFont fonte, XBrush pincel, XPoint c, double raio, bool emCima)
    {
        var larguras = texto.Select(ch => g.MeasureString(ch.ToString(), fonte).Width).ToArray();
        var total = larguras.Sum() / raio; // ângulo ocupado, em radianos
        var limite = Math.PI * 0.8;
        var escala = total > limite ? limite / total : 1; // texto longo demais é comprimido para caber no arco
        var angulo = emCima ? -Math.PI / 2 - total * escala / 2 : Math.PI / 2 + total * escala / 2;
        for (var i = 0; i < texto.Length; i++)
        {
            var passo = larguras[i] / raio * escala;
            var meio = emCima ? angulo + passo / 2 : angulo - passo / 2;
            var ponto = new XPoint(c.X + raio * Math.Cos(meio), c.Y + raio * Math.Sin(meio));
            var estado = g.Save();
            g.RotateAtTransform(meio * 180 / Math.PI + (emCima ? 90 : -90), ponto);
            g.DrawString(texto[i].ToString(), fonte, pincel, new XRect(ponto.X - 10, ponto.Y - 6, 20, 12), XStringFormats.Center);
            g.Restore(estado);
            angulo = emCima ? angulo + passo : angulo - passo;
        }
    }

    /// <summary>Quadro de assinatura: logo à esquerda, signatário e dados à direita.</summary>
    static void DesenharQuadro(XGraphics g, XRect r, DadosEstampa d)
    {
        var cor = XColor.FromArgb(255, 40, 40, 40);
        g.DrawRectangle(new XPen(XColor.FromArgb(255, 120, 120, 120), 0.8), XBrushes.White, r);
        var area = new XRect(r.X + 6, r.Y + 5, r.Width - 12, r.Height - 10);
        if (DesenharLogo(g, d.Logo, new XRect(area.X, area.Y, area.Height, area.Height)))
            area = new XRect(area.X + area.Height + 6, area.Y, area.Width - area.Height - 6, area.Height);
        var p = new XSolidBrush(cor);
        var y = area.Y + (area.Height - 53) / 2;
        Linha(g, "Assinado digitalmente por", new XFont("Liberation Sans", 7.5), p, area, ref y, 10, esquerda: true);
        Linha(g, Cortar(d.Signatario, 40), new XFont("Liberation Sans", 10, XFontStyleEx.Bold), p, area, ref y, 13, esquerda: true);
        Linha(g, "Data: " + d.Utc.ToString("dd/MM/yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture), new XFont("Liberation Sans", 7.5), p, area, ref y, 10, esquerda: true);
        Linha(g, d.Padrao + (d.Servidor is null ? "" : " · carimbo do tempo"), new XFont("Liberation Sans", 7.5), p, area, ref y, 10, esquerda: true);
        Linha(g, "Verifique em validar.iti.gov.br", new XFont("Liberation Sans", 7), p, area, ref y, 10, esquerda: true);
    }

    // os executáveis rodam sem dados de cultura (InvariantGlobalization), então os meses ficam aqui
    static readonly string[] Meses = { "JAN", "FEV", "MAR", "ABR", "MAI", "JUN", "JUL", "AGO", "SET", "OUT", "NOV", "DEZ" };

    static void Linha(XGraphics g, string texto, XFont fonte, XBrush pincel, XRect area, ref double y, double altura, bool esquerda = false)
    {
        g.DrawString(texto, fonte, pincel, new XRect(area.X, y, area.Width, altura), esquerda ? XStringFormats.CenterLeft : XStringFormats.Center);
        y += altura;
    }

    static string Cortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static bool DesenharLogo(XGraphics g, byte[]? logo, XRect caixa)
    {
        if (logo is null) return false;
        XImage img;
        try { img = XImage.FromStream(new MemoryStream(logo)); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotImplementedException or IOException)
        {
            throw new ArgumentException("Não consegui ler o logo. Use uma imagem PNG ou JPEG.", e);
        }
        using (img)
        {
            img.Interpolate = false; // /Interpolate true faz alguns leitores de PDF recusarem a página
            var escala = Math.Min(caixa.Width / img.PixelWidth, caixa.Height / img.PixelHeight);
            var (l, a) = (img.PixelWidth * escala, img.PixelHeight * escala);
            g.DrawImage(img, caixa.X + (caixa.Width - l) / 2, caixa.Y + (caixa.Height - a) / 2, l, a);
        }
        return true;
    }

    static readonly Regex RegexByteRange = new(@"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]", RegexOptions.Compiled);

    /// <summary>
    /// Confere todas as assinaturas do PDF, de qualquer programa: cada ByteRange aponta o trecho assinado
    /// e o CMS (entre &lt; e &gt;) que fica no buraco entre as duas partes.
    /// </summary>
    public static List<VerificacaoPdf> Verificar(byte[] pdf)
    {
        var texto = Encoding.Latin1.GetString(pdf);
        var lista = new List<VerificacaoPdf>();
        foreach (Match m in RegexByteRange.Matches(texto))
        {
            var v = m.Groups.Values.Skip(1).Select(gr => long.Parse(gr.Value, CultureInfo.InvariantCulture)).ToArray();
            if (v[0] != 0 || v[1] < 1 || v[2] <= v[1] || v[2] + v[3] > pdf.Length) continue; // marca reservada ou inválida
            var hex = texto.Substring((int)v[1], (int)(v[2] - v[1])).Trim().TrimStart('<').TrimEnd('>');
            byte[] cms;
            try { cms = Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex + "0"); }
            catch (FormatException) { continue; }
            var conteudo = new byte[v[1] + v[3]];
            Buffer.BlockCopy(pdf, 0, conteudo, 0, (int)v[1]);
            Buffer.BlockCopy(pdf, (int)v[2], conteudo, (int)v[1], (int)v[3]);
            var ver = AssinaturaCades.Verificar(conteudo, AparaZeros(cms));
            // o dicionário da assinatura fica em volta do ByteRange e logo depois do buraco do /Contents
            var antes = Math.Max(0, m.Index - 600);
            var dicionario = texto[antes..m.Index] + texto.Substring((int)v[2], (int)Math.Min(600, texto.Length - v[2]));
            lista.Add(new VerificacaoPdf(lista.Count + 1, ver, v[2] + v[3] == pdf.Length,
                Campo(dicionario, "Reason"), Campo(dicionario, "M")));
        }
        return lista;
    }

    /// <summary>O CMS vem seguido dos zeros que sobraram no espaço reservado.</summary>
    static byte[] AparaZeros(byte[] cms)
    {
        try
        {
            var leitor = new System.Formats.Asn1.AsnReader(cms, System.Formats.Asn1.AsnEncodingRules.BER);
            return leitor.ReadEncodedValue().ToArray();
        }
        catch (System.Formats.Asn1.AsnContentException) { return cms; }
    }

    /// <summary>Lê um texto do dicionário: literal (texto) ou hexadecimal &lt;FEFF...&gt; (UTF-16, usado para acentos).</summary>
    static string? Campo(string dicionario, string nome)
    {
        var m = Regex.Match(dicionario, @"/" + nome + @"\s*(?:\(((?:\\.|[^\\)])*)\)|<([0-9A-Fa-f]*)>)");
        if (!m.Success) return null;
        if (m.Groups[1].Success) return Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1");
        var b = Convert.FromHexString(m.Groups[2].Value.Length % 2 == 0 ? m.Groups[2].Value : m.Groups[2].Value + "0");
        return b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF ? Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2) : Encoding.Latin1.GetString(b);
    }

    /// <summary>Fontes Liberation (SIL OFL 1.1) embutidas, para a estampa sair igual no Windows e no Linux.</summary>
    sealed class Fontes : IFontResolver
    {
        static readonly object Trava = new();

        public static void Registrar()
        {
            lock (Trava)
                if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new Fontes();
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            familyName.Contains("Serif", StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo("LiberationSerif-Bold")
                : new FontResolverInfo(bold ? "LiberationSans-Bold" : "LiberationSans-Regular");

        public byte[]? GetFont(string faceName)
        {
            using var s = typeof(Fontes).Assembly.GetManifestResourceStream($"fontes.{faceName}.ttf");
            if (s is null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
