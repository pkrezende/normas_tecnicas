using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;
using Xunit;

namespace CifraToken.Testes;

public class CmsCifraTestes
{
    /// <summary>Certificado de laboratório com o mesmo Key Usage do A3 da AC Defesa.</summary>
    static X509Certificate2 Laboratorio(string nome)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={nome}, OU=Laboratorio, O=ICP-Brasil, C=BR", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyEncipherment, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        // exporta e reimporta para que a chave fique utilizável pelo CMS também no Windows
        return new X509Certificate2(cert.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    static readonly byte[] Original = RandomNumberGenerator.GetBytes(100_000);

    [Theory]
    [InlineData(PreenchimentoRsa.Pkcs1)]
    [InlineData(PreenchimentoRsa.OaepSha1)]
    public void CifraParaDoisEDecifraComCadaUm(PreenchimentoRsa preenchimento)
    {
        using var titular = Laboratorio("titular");
        using var contingencia = Laboratorio("contingencia");
        var publicos = new[] { titular, contingencia }.Select(c => new X509Certificate2(c.RawData)).ToList();

        var p7m = CmsCifra.Cifrar(Original, publicos, preenchimento);

        Assert.Equal(Original, CmsCifra.Decifrar(p7m, new X509Certificate2Collection(titular)));
        Assert.Equal(Original, CmsCifra.Decifrar(p7m, new X509Certificate2Collection(contingencia)));
        Assert.Equal(2, CmsCifra.Destinatarios(p7m).Count);
        Assert.Contains(CmsCifra.Destinatarios(p7m), d => d.Corresponde(titular));
    }

    /// <summary>
    /// Caminho usado com o token por PKCS#11: o CMS entrega a chave de sessão cifrada a um objeto RSA
    /// que não expõe a chave privada (no token, ele chama C_Decrypt).
    /// </summary>
    [Theory]
    [InlineData(PreenchimentoRsa.Pkcs1)]
    [InlineData(PreenchimentoRsa.OaepSha1)]
    public void DecifraComRsaQueNaoExpoeAChave(PreenchimentoRsa preenchimento)
    {
        using var titular = Laboratorio("titular");
        var p7m = CmsCifra.Cifrar(Original, new[] { new X509Certificate2(titular.RawData) }, preenchimento);

        using var opaca = new RsaOpaca(titular.GetRSAPrivateKey()!);
        Assert.Equal(Original, CmsCifra.Decifrar(p7m, new X509Certificate2(titular.RawData), opaca));
        Assert.Equal(1, opaca.Chamadas);
    }

    [Fact]
    public void RecusaCertificadoQueNaoEDestinatario()
    {
        using var titular = Laboratorio("titular");
        using var outro = Laboratorio("outro");
        var p7m = CmsCifra.Cifrar(Original, new[] { new X509Certificate2(titular.RawData) });
        Assert.ThrowsAny<CryptographicException>(() =>
            CmsCifra.Decifrar(p7m, new X509Certificate2(outro.RawData), outro.GetRSAPrivateKey()!));
    }

    [Fact]
    public void LePem()
    {
        using var titular = Laboratorio("titular");
        var der = CmsCifra.Cifrar(Original, new[] { new X509Certificate2(titular.RawData) });
        var pem = System.Text.Encoding.ASCII.GetBytes(PemEncoding.WriteString("CMS", der));
        Assert.Equal(der, CmsCifra.DerOuPem(pem));
    }

    [Fact]
    public void DetectaCertificadoSemKeyEncipherment()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=so assinatura", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(1));
        Assert.False(CmsCifra.PermiteCifrar(cert));
    }

    /// <summary>
    /// Caminho do token pelo repositório do Windows (CSP/minidriver): o certificado com chave fica em
    /// CurrentUser\My e o CMS acha a chave sozinho. Só roda no Windows.
    /// </summary>
    [Fact]
    public void DecifraPeloRepositorioDoWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = Laboratorio("repositorio");
        using var cert = new X509Certificate2(temp.Export(X509ContentType.Pfx, "x"), "x",
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        try
        {
            var p7m = CmsCifra.Cifrar(Original, new[] { new X509Certificate2(cert.RawData) });
            Assert.Equal(Original, CmsCifra.Decifrar(p7m));
            Assert.Contains(Repositorio.Pessoais(), c => c.Thumbprint == cert.Thumbprint);
        }
        finally
        {
            store.Remove(cert);
        }
    }

    sealed class RsaOpaca : RSA
    {
        readonly RSA real;
        public int Chamadas;
        public RsaOpaca(RSA real) { this.real = real; KeySizeValue = real.KeySize; LegalKeySizesValue = real.LegalKeySizes; }
        public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding) { Chamadas++; return real.Decrypt(data, padding); }
        public override RSAParameters ExportParameters(bool includePrivateParameters) =>
            includePrivateParameters ? throw new CryptographicException("opaca") : real.ExportParameters(false);
        public override void ImportParameters(RSAParameters parameters) => throw new NotSupportedException();
    }
}
