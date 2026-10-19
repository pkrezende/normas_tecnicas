using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;

namespace CifraToken.Core;

/// <summary>Preenchimento usado para cifrar a chave de sessão com o RSA do destinatário.</summary>
public enum PreenchimentoRsa
{
    /// <summary>RSA PKCS#1 v1.5 (rsaEncryption). Padrão do openssl cms e aceito por qualquer token.</summary>
    Pkcs1,
    /// <summary>RSA-OAEP com SHA-1 (o mesmo de "openssl cms -keyopt rsa_padding_mode:oaep"). O token precisa suportar.</summary>
    OaepSha1,
}

/// <summary>
/// Cifra e decifra arquivos no formato CMS EnvelopedData (RFC 5652), o mesmo de
/// "openssl cms -encrypt -binary -aes256 -outform DER".
/// O conteúdo é cifrado com AES-256-CBC e a chave de sessão com o RSA de cada destinatário.
/// </summary>
public static class CmsCifra
{
    /// <summary>OID do AES-256-CBC (NIST, 2.16.840.1.101.3.4.1.42).</summary>
    public const string OidAes256Cbc = "2.16.840.1.101.3.4.1.42";

    public static byte[] Cifrar(byte[] conteudo, IEnumerable<X509Certificate2> destinatarios,
        PreenchimentoRsa preenchimento = PreenchimentoRsa.Pkcs1)
    {
        var lista = destinatarios.ToList();
        if (lista.Count == 0)
            throw new ArgumentException("Escolha pelo menos um certificado de destinatário.");

        var cms = new EnvelopedCms(new ContentInfo(conteudo),
            new AlgorithmIdentifier(new Oid(OidAes256Cbc)));

        var recipients = new CmsRecipientCollection();
        foreach (var cert in lista)
        {
            if (cert.GetRSAPublicKey() is null)
                throw new ArgumentException($"O certificado \"{Nome(cert)}\" não tem chave RSA.");
            recipients.Add(preenchimento == PreenchimentoRsa.Pkcs1
                ? new CmsRecipient(SubjectIdentifierType.IssuerAndSerialNumber, cert, RSAEncryptionPadding.Pkcs1)
                : new CmsRecipient(SubjectIdentifierType.IssuerAndSerialNumber, cert, RSAEncryptionPadding.OaepSHA1));
        }
        cms.Encrypt(recipients);
        return cms.Encode();
    }

    /// <summary>
    /// Decifra. Sem <paramref name="certificadosComChave"/>, procura a chave nos repositórios
    /// "Pessoal" do usuário e da máquina, onde o middleware do token registra o certificado;
    /// o próprio middleware pede o PIN.
    /// </summary>
    public static byte[] Decifrar(byte[] p7m, X509Certificate2Collection? certificadosComChave = null)
    {
        var cms = Decodificar(p7m);
        if (certificadosComChave is null || certificadosComChave.Count == 0)
            cms.Decrypt();
        else
            cms.Decrypt(certificadosComChave);
        return cms.ContentInfo.Content;
    }

    /// <summary>
    /// Decifra com a primeira das chaves que for destinatária do arquivo. Primeiro pelo emissor e nº de série;
    /// se nenhum confere (destinatário por identificador de chave, ou nome do emissor escrito de outro jeito),
    /// tenta cada chave em cada destinatário: com a chave errada o RSA do token recusa o bloco.
    /// </summary>
    public static byte[] DecifrarComChaves(byte[] p7m, IReadOnlyList<(X509Certificate2 Cert, RSA Chave)> chaves)
    {
        var destinos = Destinatarios(p7m);
        foreach (var (cert, chave) in chaves)
            if (destinos.Any(d => d.Corresponde(cert)))
                return Decifrar(p7m, cert, chave);

        foreach (var (cert, chave) in chaves.Where(c => PermiteCifrar(c.Cert)))
        {
            var cms = Decodificar(p7m);
            foreach (RecipientInfo ri in cms.RecipientInfos)
            {
                try
                {
                    cms.Decrypt(ri, chave);
                    return cms.ContentInfo.Content;
                }
                catch (CryptographicException) { /* não era este destinatário */ }
            }
        }
        throw new CryptographicException(
            "O arquivo não foi cifrado para a credencial conectada.\n" +
            "Cifrado para: " + string.Join("; ", destinos.Select(d => d.Resumo)) + "\n" +
            "Credencial: " + string.Join("; ", chaves.Select(c => $"{Nome(c.Cert)} (nº de série {c.Cert.SerialNumber})")) + "\n" +
            "Se o seu certificado foi renovado, o arquivo pode ter sido cifrado para o anterior.");
    }

    /// <summary>
    /// Decifra com uma chave privada explícita (por exemplo, a do token via PKCS#11),
    /// escolhendo o destinatário do arquivo que corresponde ao certificado.
    /// </summary>
    public static byte[] Decifrar(byte[] p7m, X509Certificate2 certificado, RSA chavePrivada)
    {
        var cms = Decodificar(p7m);
        var ri = cms.RecipientInfos.Cast<RecipientInfo>().FirstOrDefault(r =>
                     r.RecipientIdentifier.Value is X509IssuerSerial s
                     && new Destinatario(s.IssuerName, s.SerialNumber).Corresponde(certificado))
                 ?? throw new CryptographicException(
                     $"O arquivo não foi cifrado para o certificado \"{Nome(certificado)}\" (nº de série {certificado.SerialNumber}).");
        cms.Decrypt(ri, chavePrivada);
        return cms.ContentInfo.Content;
    }

    public static EnvelopedCms Decodificar(byte[] p7m)
    {
        var cms = new EnvelopedCms();
        cms.Decode(DerOuPem(p7m));
        return cms;
    }

    /// <summary>Para quem o arquivo foi cifrado (emissor e número de série de cada destinatário).</summary>
    public static IReadOnlyList<Destinatario> Destinatarios(byte[] p7m)
    {
        var saida = new List<Destinatario>();
        foreach (RecipientInfo ri in Decodificar(p7m).RecipientInfos)
        {
            var id = ri.RecipientIdentifier;
            saida.Add(id.Type == SubjectIdentifierType.IssuerAndSerialNumber && id.Value is X509IssuerSerial s
                ? new Destinatario(s.IssuerName, s.SerialNumber)
                : new Destinatario($"{id.Type}: {id.Value}", ""));
        }
        return saida;
    }

    /// <summary>Indica se o certificado permite cifrar (Key Usage com keyEncipherment, ou sem Key Usage).</summary>
    public static bool PermiteCifrar(X509Certificate2 cert)
    {
        var ku = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return ku is null || ku.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment);
    }

    public static string Nome(X509Certificate2 cert)
    {
        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        return string.IsNullOrWhiteSpace(cn) ? cert.Subject : cn;
    }

    /// <summary>Lê um certificado em .cer/.crt/.pem (DER ou PEM).</summary>
    public static X509Certificate2 LerCertificado(string caminho)
    {
        var bytes = File.ReadAllBytes(caminho);
        var texto = Encoding.ASCII.GetString(bytes);
        if (texto.Contains("-----BEGIN CERTIFICATE-----"))
            return X509Certificate2.CreateFromPem(texto);
        return X509CertificateLoader.LoadCertificate(bytes);
    }

    /// <summary>Aceita o .p7m em DER ou em PEM (BEGIN CMS / BEGIN PKCS7).</summary>
    public static byte[] DerOuPem(byte[] dados)
    {
        if (dados.Length > 0 && dados[0] == 0x30)
            return dados;
        var texto = Encoding.ASCII.GetString(dados);
        var inicio = texto.IndexOf("-----BEGIN ", StringComparison.Ordinal);
        if (inicio < 0)
            return dados;
        var fimCab = texto.IndexOf('\n', inicio);
        var rodape = texto.IndexOf("-----END ", fimCab, StringComparison.Ordinal);
        var b64 = texto[(fimCab + 1)..rodape];
        return Convert.FromBase64String(string.Concat(b64.Where(c => !char.IsWhiteSpace(c))));
    }
}

public sealed record Destinatario(string Emissor, string NumeroSerie)
{
    /// <summary>Mesmo nº de série e mesmo emissor (comparado sem diferenciar espaços e maiúsculas).</summary>
    public bool Corresponde(X509Certificate2 cert) =>
        NumeroSerie.Length > 0
        && NumeroSerie.TrimStart('0').Equals(cert.SerialNumber.TrimStart('0'), StringComparison.OrdinalIgnoreCase)
        && (Emissor == cert.IssuerName.Name
            || Normalizar(Emissor) == Normalizar(cert.IssuerName.Name)
            || Normalizar(Emissor) == Normalizar(cert.IssuerName.Format(false)));

    static string Normalizar(string dn) =>
        new string(dn.Where(c => !char.IsWhiteSpace(c) && c != '"').ToArray()).ToUpperInvariant();

    /// <summary>Emissor (só o CN) e nº de série, para mensagens.</summary>
    public string Resumo
    {
        get
        {
            if (NumeroSerie.Length == 0) return Emissor;
            var cn = Emissor.Split(", ").FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))?[3..] ?? Emissor;
            return $"certificado emitido por {cn}, nº de série {NumeroSerie}";
        }
    }

    public override string ToString() =>
        NumeroSerie.Length == 0 ? Emissor : $"Emissor: {Emissor} | Nº de série: {NumeroSerie}";
}
