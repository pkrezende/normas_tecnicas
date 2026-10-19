using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace CifraToken.Core;

/// <summary>Resultado da verificação de uma assinatura CAdES.</summary>
public sealed record Verificacao(
    bool Integra, string Signatario, X509Certificate2? Certificado, string? Politica,
    DateTimeOffset? Carimbo, string? Act, bool? CarimboValido, string? CadeiaStatus, string? Erro);

/// <summary>
/// Assinatura digital destacada (.p7s) no formato CAdES (CMS SignedData, RFC 5652), com os atributos exigidos
/// pelas Políticas de Assinatura da ICP-Brasil (DOC-ICP-15.03 v9.0): signing-certificate-v2 e sigPolicyId;
/// para AD-RT, carimbo do tempo da assinatura (signatureTimeStampToken, RFC 3161).
/// </summary>
public static class AssinaturaCades
{
    public const string OidSigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";
    public const string OidSigPolicyId = "1.2.840.113549.1.9.16.2.15";
    public const string OidSignatureTimeStamp = "1.2.840.113549.1.9.16.2.14";
    const string OidSpqUri = "1.2.840.113549.1.9.16.5.1";
    const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    const string OidSha512 = "2.16.840.1.101.3.4.2.3";

    /// <summary>
    /// Assina o conteúdo. Com <paramref name="chave"/> nula, usa a chave associada ao certificado
    /// (repositório do Windows ou .pfx). <paramref name="cadeia"/> são certificados extras a incluir (ACs).
    /// </summary>
    public static async Task<byte[]> Assinar(byte[] conteudo, X509Certificate2 certificado, RSA? chave,
        PoliticaIcp? politica, string? urlAct, HttpClient? http, IEnumerable<X509Certificate2>? cadeia = null,
        string? politicaAct = null, CancellationToken ct = default)
    {
        if (politica?.ExigeCarimbo == true && string.IsNullOrWhiteSpace(urlAct))
            throw new ArgumentException("A política AD-RT exige carimbo do tempo: configure o endereço da ACT.");

        var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
        var signer = chave is null
            ? new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificado)
            : new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificado, chave);
        signer.DigestAlgorithm = new Oid(OidSha256);
        signer.IncludeOption = X509IncludeOption.EndCertOnly;
        if (cadeia is not null)
            foreach (var c in cadeia.Where(c => c.Thumbprint != certificado.Thumbprint && c.Subject != c.Issuer))
                signer.Certificates.Add(c);
        signer.SignedAttributes.Add(new AsnEncodedData(OidSigningCertificateV2, SigningCertificateV2(certificado)));
        if (politica is not null)
            signer.SignedAttributes.Add(new AsnEncodedData(OidSigPolicyId, SigPolicyId(politica)));
        else
            signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));

        cms.ComputeSignature(signer, silent: true);

        if (!string.IsNullOrWhiteSpace(urlAct))
        {
            var si = cms.SignerInfos[0];
            var token = await CarimboTempo.Solicitar(si.GetSignature(), urlAct, http ?? new HttpClient(), politicaAct, ct);
            si.AddUnsignedAttribute(new AsnEncodedData(OidSignatureTimeStamp, token.AsSignedCms().Encode()));
        }
        return cms.Encode();
    }

    /// <summary>SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 } (RFC 5035), com SHA-256 (padrão, omitido) e issuerSerial.</summary>
    static byte[] SigningCertificateV2(X509Certificate2 cert)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        using (w.PushSequence())
        using (w.PushSequence())
        {
            w.WriteOctetString(SHA256.HashData(cert.RawData));
            using (w.PushSequence())
            {
                using (w.PushSequence())
                {
                    // GeneralName directoryName [4] EXPLICIT Name
                    var tag = new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true);
                    using (w.PushSequence(tag)) w.WriteEncodedValue(cert.IssuerName.RawData);
                }
                w.WriteIntegerUnsigned(Convert.FromHexString(cert.SerialNumber));
            }
        }
        return w.Encode();
    }

    /// <summary>SignaturePolicyId ::= SEQUENCE { sigPolicyId, sigPolicyHash, sigPolicyQualifiers (URI da política) } (RFC 5126, 5.8.1).</summary>
    static byte[] SigPolicyId(PoliticaIcp p)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteObjectIdentifier(p.Oid);
            using (w.PushSequence())
            {
                using (w.PushSequence()) w.WriteObjectIdentifier(p.AlgoritmoResumo == HashAlgorithmName.SHA512 ? OidSha512 : OidSha256);
                w.WriteOctetString(p.Resumo);
            }
            using (w.PushSequence())
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(OidSpqUri);
                w.WriteCharacterString(UniversalTagNumber.IA5String, p.Url);
            }
        }
        return w.Encode();
    }

    /// <summary>
    /// Confere a integridade da assinatura destacada e do carimbo do tempo. A cadeia é montada com os certificados
    /// do sistema e os incluídos na assinatura; a revogação não é consultada.
    /// </summary>
    public static Verificacao Verificar(byte[] conteudo, byte[] p7s)
    {
        var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
        try
        {
            cms.Decode(CmsCifra.DerOuPem(p7s));
        }
        catch (CryptographicException e)
        {
            return new Verificacao(false, "", null, null, null, null, null, null, "Não é uma assinatura CMS válida: " + e.Message);
        }
        var si = cms.SignerInfos[0];
        var cert = si.Certificate;
        var nome = cert is null ? "(certificado não incluído)" : CmsCifra.Nome(cert);
        string? erro = null;
        try { si.CheckSignature(new X509Certificate2Collection(), verifySignatureOnly: true); }
        catch (CryptographicException e) { erro = "A assinatura NÃO confere com o arquivo: " + e.Message; }

        string? politica = null;
        foreach (var a in si.SignedAttributes)
            if (a.Oid?.Value == OidSigPolicyId)
                politica = new AsnReader(a.Values[0].RawData, AsnEncodingRules.DER).ReadSequence().ReadObjectIdentifier();

        DateTimeOffset? hora = null;
        string? act = null;
        bool? carimboOk = null;
        foreach (var a in si.UnsignedAttributes)
        {
            if (a.Oid?.Value != OidSignatureTimeStamp) continue;
            if (Rfc3161TimestampToken.TryDecode(a.Values[0].RawData, out var token, out _) && token is not null)
            {
                hora = token.TokenInfo.Timestamp;
                carimboOk = token.VerifySignatureForSignerInfo(si, out var actCert);
                act = actCert is null ? null : CmsCifra.Nome(actCert);
            }
            else carimboOk = false;
        }

        string? cadeia = null;
        if (cert is not null)
        {
            using var ch = new X509Chain();
            ch.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            ch.ChainPolicy.ExtraStore.AddRange(cms.Certificates);
            if (hora is { } h) ch.ChainPolicy.VerificationTime = h.LocalDateTime;
            cadeia = ch.Build(cert)
                ? "Cadeia válida até uma raiz confiável deste computador (revogação não consultada)."
                : "Cadeia não validada: " + string.Join("; ", ch.ChainStatus.Select(s => s.StatusInformation.Trim()).Distinct()) +
                  " (instale as ACs da ICP-Brasil para validar).";
        }
        return new Verificacao(erro is null, nome, cert, politica, hora, act, carimboOk, cadeia, erro);
    }
}
