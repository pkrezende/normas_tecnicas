using System.Security.Cryptography;
using System.Text;
using CifraToken.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace CifraToken.Testes;

public class PdfTestes
{
    static byte[] PdfSimples(int paginas = 2)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < paginas; i++)
        {
            var p = doc.AddPage();
            using var g = XGraphics.FromPdfPage(p);
            g.DrawRectangle(XBrushes.LightGray, 50, 50 + i * 20, 200, 100);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    static byte[] Logo()
    {
        // PNG 2x2 vermelho, para não depender de arquivo
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGM8wcDAwMDAxMDAwMDAAAAKUgDMUCehPAAAAABJRU5ErkJggg==");
    }

    [Theory]
    [InlineData(EstiloEstampa.Carimbo)]
    [InlineData(EstiloEstampa.Assinatura)]
    [InlineData(EstiloEstampa.Nenhuma)]
    public async Task AssinaEVerificaPdf(EstiloEstampa estilo)
    {
        using var cert = AssinaturaTestes.Signatario();
        var pdf = await AssinaturaPdf.Assinar(PdfSimples(), cert, null, null, null, null,
            new Estampa(estilo, 1, PosicaoEstampa.InferiorDireito, Logo(), "Aprovo", "Brasília"));
        Assert.True(AssinaturaPdf.EhPdf(pdf));
        Assert.True(AssinaturaPdf.JaAssinado(pdf));
        Assert.Contains("/ETSI.CAdES.detached", Encoding.Latin1.GetString(pdf));
        var v = Assert.Single(AssinaturaPdf.Verificar(pdf));
        Assert.True(v.Assinatura.Integra, v.Assinatura.Erro);
        Assert.True(v.CobreDocumentoInteiro);
        Assert.Equal("Signatario de Teste", v.Assinatura.Signatario);
        Assert.Equal("Aprovo", v.Motivo);
        Assert.StartsWith("D:", v.Data);
    }

    [Fact]
    public async Task PoliticaPadesNoCms()
    {
        using var cert = AssinaturaTestes.Signatario();
        var pol = PoliticaIcp.Ler(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "dados", "PA_AD_RB_v2_3.der")), "AD-RB", "http://x/PA_AD_RB_v2_3.der");
        var pdf = await AssinaturaPdf.Assinar(PdfSimples(1), cert, null, pol, null, null, new Estampa(EstiloEstampa.Carimbo));
        var v = Assert.Single(AssinaturaPdf.Verificar(pdf));
        Assert.Equal(pol.Oid, v.Assinatura.Politica);
    }

    [Fact]
    public async Task AlteracaoNoPdfInvalidaAssinatura()
    {
        using var cert = AssinaturaTestes.Signatario();
        var pdf = await AssinaturaPdf.Assinar(PdfSimples(), cert, null, null, null, null, new Estampa(EstiloEstampa.Carimbo));
        pdf[300] ^= 0x01; // antes do buraco da assinatura
        Assert.False(Assert.Single(AssinaturaPdf.Verificar(pdf)).Assinatura.Integra);
    }

    [Fact]
    public async Task AcrescimoDepoisDaAssinaturaEhApontado()
    {
        using var cert = AssinaturaTestes.Signatario();
        var pdf = await AssinaturaPdf.Assinar(PdfSimples(), cert, null, null, null, null, new Estampa(EstiloEstampa.Nenhuma));
        var alterado = pdf.Concat(Encoding.ASCII.GetBytes("\n% acrescimo\n")).ToArray();
        var v = Assert.Single(AssinaturaPdf.Verificar(alterado));
        Assert.True(v.Assinatura.Integra);
        Assert.False(v.CobreDocumentoInteiro);
    }

    [Fact]
    public async Task RecusaPdfJaAssinadoENaoPdf()
    {
        using var cert = AssinaturaTestes.Signatario();
        var pdf = await AssinaturaPdf.Assinar(PdfSimples(), cert, null, null, null, null, new Estampa(EstiloEstampa.Nenhuma));
        await Assert.ThrowsAsync<ArgumentException>(() => AssinaturaPdf.Assinar(pdf, cert, null, null, null, null, new Estampa(EstiloEstampa.Nenhuma)));
        await Assert.ThrowsAsync<ArgumentException>(() => AssinaturaPdf.Assinar(RandomNumberGenerator.GetBytes(100), cert, null, null, null, null, new Estampa(EstiloEstampa.Nenhuma)));
    }
}
