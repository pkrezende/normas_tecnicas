using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;

namespace CifraToken.Core;

/// <summary>Pedido de carimbo do tempo a uma ACT pelo protocolo da RFC 3161 sobre HTTP.</summary>
public static class CarimboTempo
{
    /// <summary>Endereço da ACT Defesa (DPCT ACT Defesa v1.0, 4.2.5, a). Só atende IPs cadastrados das redes do MD e das Forças.</summary>
    public const string UrlActDefesa = "https://sct.acdefesa.mil.br/timestamp";

    public static async Task<Rfc3161TimestampToken> Solicitar(byte[] dados, string url, HttpClient http,
        string? politicaOid = null, CancellationToken ct = default)
    {
        var pedido = Rfc3161TimestampRequest.CreateFromData(dados, HashAlgorithmName.SHA256,
            requestedPolicyId: string.IsNullOrWhiteSpace(politicaOid) ? null : new Oid(politicaOid),
            nonce: RandomNumberGenerator.GetBytes(8), requestSignerCertificates: true);
        using var corpo = new ByteArrayContent(pedido.Encode());
        corpo.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using var resposta = await http.PostAsync(url, corpo, ct);
        if (!resposta.IsSuccessStatusCode)
            throw new CryptographicException($"A ACT respondeu HTTP {(int)resposta.StatusCode} ({resposta.ReasonPhrase}).");
        var bytes = await resposta.Content.ReadAsByteArrayAsync(ct);
        try
        {
            return pedido.ProcessResponse(bytes, out _);
        }
        catch (CryptographicException e)
        {
            throw new CryptographicException("A ACT recusou o pedido ou a resposta é inválida: " + e.Message, e);
        }
    }
}
