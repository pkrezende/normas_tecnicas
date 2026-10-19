using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;
using Xunit;

namespace CifraToken.Testes;

public class AssinaturaTestes
{
    static string Dados(string nome) => Path.Combine(AppContext.BaseDirectory, "dados", nome);

    static X509Certificate2 Signatario()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Signatario de Teste, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        using var c = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    /// <summary>Arquivo oficial da PA AD-RB v2.3 do ITI: OID e resumo interno (signPolicyHash).</summary>
    [Fact]
    public void LePoliticaOficialAdRb()
    {
        var p = PoliticaIcp.Ler(File.ReadAllBytes(Dados("PA_AD_RB_v2_3.der")), "AD-RB v2.3", "http://politicas.icpbrasil.gov.br/PA_AD_RB_v2_3.der");
        Assert.Equal("2.16.76.1.7.1.1.2.3", p.Oid);
        Assert.Equal("B16E88BBF77322A67995B79078778ED3D0EA7C88587B6F6D518B715E8F76A3D5", Convert.ToHexString(p.Resumo));
        Assert.False(p.ExigeCarimbo);
    }

    [Fact]
    public void LePoliticaOficialAdRt()
    {
        var p = PoliticaIcp.Ler(File.ReadAllBytes(Dados("PA_AD_RT_v2_3.der")), "AD-RT v2.3", "http://politicas.icpbrasil.gov.br/PA_AD_RT_v2_3.der");
        Assert.StartsWith("2.16.76.1.7.1.2.", p.Oid);
        Assert.True(p.ExigeCarimbo);
    }

    [Fact]
    public void RecusaPoliticaAlterada()
    {
        var der = File.ReadAllBytes(Dados("PA_AD_RB_v2_3.der"));
        der[200] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => PoliticaIcp.Ler(der, "x", "x"));
    }

    [Fact]
    public async Task AssinaComPoliticaEVerifica()
    {
        using var cert = Signatario();
        var pol = PoliticaIcp.Ler(File.ReadAllBytes(Dados("PA_AD_RB_v2_3.der")), "AD-RB", "http://politicas.icpbrasil.gov.br/PA_AD_RB_v2_3.der");
        var conteudo = RandomNumberGenerator.GetBytes(20_000);
        var p7s = await AssinaturaCades.Assinar(conteudo, cert, null, pol, null, null);

        var v = AssinaturaCades.Verificar(conteudo, p7s);
        Assert.True(v.Integra, v.Erro);
        Assert.Equal("2.16.76.1.7.1.1.2.3", v.Politica);
        Assert.Equal("Signatario de Teste", v.Signatario);

        // atributos assinados exigidos pela PA (DOC-ICP-15.03): content-type, message-digest, signing-certificate-v2, sigPolicyId
        var cms = new SignedCms(new ContentInfo(conteudo), true);
        cms.Decode(p7s);
        var oids = cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid!.Value).ToList();
        Assert.Contains("1.2.840.113549.1.9.3", oids);
        Assert.Contains("1.2.840.113549.1.9.4", oids);
        Assert.Contains(AssinaturaCades.OidSigningCertificateV2, oids);
        Assert.Contains(AssinaturaCades.OidSigPolicyId, oids);

        // signing-certificate-v2 aponta para o SHA-256 do certificado do signatário
        var scv2 = cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().First(a => a.Oid!.Value == AssinaturaCades.OidSigningCertificateV2);
        var ess = new AsnReader(scv2.Values[0].RawData, AsnEncodingRules.DER).ReadSequence().ReadSequence().ReadSequence();
        Assert.Equal(SHA256.HashData(cert.RawData), ess.ReadOctetString());

        conteudo[0] ^= 1;
        Assert.False(AssinaturaCades.Verificar(conteudo, p7s).Integra);
    }

    /// <summary>Assinatura com chave que não sai do "token" (o CMS chama SignHash, como no PKCS#11).</summary>
    [Fact]
    public async Task AssinaComChaveOpaca()
    {
        using var cert = Signatario();
        using var opaca = new RsaQueSoAssina(cert.GetRSAPrivateKey()!);
        var conteudo = RandomNumberGenerator.GetBytes(1000);
        var p7s = await AssinaturaCades.Assinar(conteudo, X509CertificateLoader.LoadCertificate(cert.RawData), opaca, null, null, null);
        Assert.True(AssinaturaCades.Verificar(conteudo, p7s).Integra);
        Assert.Equal(1, opaca.Chamadas);
    }

    [Fact]
    public async Task AdRtSemActERecusada()
    {
        using var cert = Signatario();
        var pol = PoliticaIcp.Ler(File.ReadAllBytes(Dados("PA_AD_RT_v2_3.der")), "AD-RT", "u");
        await Assert.ThrowsAsync<ArgumentException>(() => AssinaturaCades.Assinar(new byte[1], cert, null, pol, null, null));
    }

    sealed class RsaQueSoAssina : RSA
    {
        readonly RSA real;
        public int Chamadas;
        public RsaQueSoAssina(RSA real) { this.real = real; KeySizeValue = real.KeySize; LegalKeySizesValue = real.LegalKeySizes; }
        public override byte[] SignHash(byte[] hash, HashAlgorithmName h, RSASignaturePadding p) { Chamadas++; return real.SignHash(hash, h, p); }
        public override RSAParameters ExportParameters(bool includePrivateParameters) =>
            includePrivateParameters ? throw new CryptographicException("opaca") : real.ExportParameters(false);
        public override void ImportParameters(RSAParameters parameters) => throw new NotSupportedException();
    }
}
