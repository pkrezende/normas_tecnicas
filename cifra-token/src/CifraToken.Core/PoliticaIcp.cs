using System.Formats.Asn1;
using System.Security.Cryptography;

namespace CifraToken.Core;

/// <summary>
/// Política de Assinatura (PA) da ICP-Brasil (DOC-ICP-15.03), lida do arquivo .der publicado pelo ITI.
/// O identificador (OID) e o resumo que vão na assinatura (sigPolicyId / sigPolicyHash) são tirados do próprio
/// arquivo, que é baixado de politicas.icpbrasil.gov.br e conferido com o "-sha256.txt" publicado ao lado.
/// </summary>
public sealed record PoliticaIcp(string Nome, string Oid, string Url, HashAlgorithmName AlgoritmoResumo, byte[] Resumo)
{
    /// <summary>Políticas CAdES vigentes conferidas no repositório do ITI em 19/10/2026 (ciclo 12/06/2025 a 22/10/2037).</summary>
    public static readonly (string Nome, string Url)[] Conhecidas =
    {
        ("AD-RB v2.4 (referência básica)", "http://politicas.icpbrasil.gov.br/PA_AD_RB_v2_4.der"),
        ("AD-RT v2.4 (referência de tempo: exige carimbo do tempo)", "http://politicas.icpbrasil.gov.br/PA_AD_RT_v2_4.der"),
    };

    public static string PastaCache => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CifraToken", "politicas");

    /// <summary>"AD-RB", "AD-RT", um caminho de arquivo .der ou um URL.</summary>
    public static async Task<PoliticaIcp> Resolver(string nomeOuCaminho, HttpClient http, CancellationToken ct = default)
    {
        if (File.Exists(nomeOuCaminho))
            return Ler(await File.ReadAllBytesAsync(nomeOuCaminho, ct), Path.GetFileName(nomeOuCaminho), nomeOuCaminho);
        var conhecida = Conhecidas.FirstOrDefault(c => c.Nome.StartsWith(nomeOuCaminho, StringComparison.OrdinalIgnoreCase));
        if (conhecida.Url is not null) return await Obter(conhecida.Nome, conhecida.Url, PastaCache, http, ct);
        if (Uri.TryCreate(nomeOuCaminho, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http"))
            return await Obter(Path.GetFileName(u.LocalPath), nomeOuCaminho, PastaCache, http, ct);
        throw new ArgumentException($"Política desconhecida: {nomeOuCaminho}. Use AD-RB, AD-RT ou o caminho de um arquivo .der.");
    }

    /// <summary>AD-RT (OID 2.16.76.1.7.1.2.x no CAdES) exige o carimbo do tempo da assinatura.</summary>
    public bool ExigeCarimbo => Oid.StartsWith("2.16.76.1.7.1.2.", StringComparison.Ordinal)
                                || Url.Contains("_AD_RT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lê o arquivo .der da política e confere o resumo interno (signPolicyHash) com o calculado.</summary>
    /// <summary>"AD-RB v2.4 (2.16.76.1.7.1.1.2.4)" para os OIDs CAdES da ICP-Brasil; senão, o próprio OID.</summary>
    public static string Descrever(string oid)
    {
        var tipo = oid.StartsWith("2.16.76.1.7.1.1.", StringComparison.Ordinal) ? "AD-RB"
                 : oid.StartsWith("2.16.76.1.7.1.2.", StringComparison.Ordinal) ? "AD-RT" : null;
        var arcos = oid.Split('.');
        return tipo is null || arcos.Length != 9 ? oid : $"{tipo} v{arcos[7]}.{arcos[8]} da ICP-Brasil ({oid})";
    }

    public static PoliticaIcp Ler(byte[] der, string nome, string url)
    {
        var leitor = new AsnReader(der, AsnEncodingRules.DER);
        var politica = leitor.ReadSequence();
        var algBruto = politica.PeekEncodedValue().ToArray();
        var algoritmo = politica.ReadSequence();
        var oidResumo = algoritmo.ReadObjectIdentifier();
        var infoBruta = politica.PeekEncodedValue().ToArray();
        var info = politica.ReadSequence();
        var oid = info.ReadObjectIdentifier();
        byte[]? resumoDeclarado = politica.HasData ? politica.ReadOctetString() : null;

        var hash = oidResumo switch
        {
            "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
            "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
            "1.3.14.3.2.26" => HashAlgorithmName.SHA1,
            _ => throw new CryptographicException($"Algoritmo de resumo da política não suportado: {oidResumo}."),
        };
        // Nos arquivos do ITI o signPolicyHash é o resumo de signPolicyHashAlg seguido de signPolicyInfo
        // (conferido no PA_AD_RB_v2_3.der); é esse valor que vai no sigPolicyHash da assinatura.
        var calculado = Resumir(hash, algBruto.Concat(infoBruta).ToArray());
        if (resumoDeclarado is not null && !resumoDeclarado.AsSpan().SequenceEqual(calculado))
            throw new CryptographicException("O resumo declarado no arquivo da política não confere. O arquivo pode estar corrompido.");
        return new PoliticaIcp(nome, oid, url, hash, calculado);
    }

    static byte[] Resumir(HashAlgorithmName h, byte[] dados) =>
        h == HashAlgorithmName.SHA512 ? SHA512.HashData(dados) : h == HashAlgorithmName.SHA1 ? SHA1.HashData(dados) : SHA256.HashData(dados);

    /// <summary>
    /// Obtém a política: usa a cópia em <paramref name="pastaCache"/> ou baixa do ITI, conferindo o SHA-256 do arquivo
    /// com o arquivo "-sha256.txt" publicado (quando ele estiver acessível).
    /// </summary>
    public static async Task<PoliticaIcp> Obter(string nome, string url, string pastaCache, HttpClient http, CancellationToken ct = default)
    {
        Directory.CreateDirectory(pastaCache);
        var local = Path.Combine(pastaCache, Path.GetFileName(new Uri(url).LocalPath));
        if (!File.Exists(local))
        {
            var der = await http.GetByteArrayAsync(url, ct);
            var urlSha = url[..^4] + "der-sha256.txt";
            try
            {
                var publicado = (await http.GetStringAsync(urlSha, ct)).Trim().Split(' ', '\t', '\r', '\n')[0];
                if (!publicado.Equals(Convert.ToHexString(SHA256.HashData(der)), StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException($"O SHA-256 do arquivo baixado não confere com o publicado em {urlSha}.");
            }
            catch (HttpRequestException) { /* arquivo de conferência indisponível: vale o resumo interno da política */ }
            Ler(der, nome, url); // valida antes de guardar
            await File.WriteAllBytesAsync(local, der, ct);
        }
        return Ler(await File.ReadAllBytesAsync(local, ct), nome, url);
    }
}
