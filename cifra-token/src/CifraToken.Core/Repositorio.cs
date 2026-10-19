using System.Security.Cryptography.X509Certificates;

namespace CifraToken.Core;

/// <summary>Acesso ao repositório de certificados do usuário (no Windows, certmgr.msc).</summary>
public static class Repositorio
{
    /// <summary>Certificados em "Pessoal" (My). Os do token aparecem aqui quando o middleware está instalado e o token conectado.</summary>
    public static List<X509Certificate2> Pessoais() => Ler(StoreName.My);

    /// <summary>Certificados em "Outras Pessoas" (AddressBook), útil para destinatários.</summary>
    public static List<X509Certificate2> OutrasPessoas() => Ler(StoreName.AddressBook);

    public static X509Certificate2 PorImpressao(string impressao)
    {
        var alvo = impressao.Replace(" ", "").Replace(":", "");
        return Pessoais().Concat(OutrasPessoas())
            .FirstOrDefault(c => c.Thumbprint.Equals(alvo, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Certificado {impressao} não encontrado no repositório.");
    }

    static List<X509Certificate2> Ler(StoreName nome)
    {
        try
        {
            using var store = new X509Store(nome, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            return store.Certificates.Cast<X509Certificate2>()
                .Where(c => c.GetRSAPublicKey() is not null)
                .OrderBy(c => CmsCifra.Nome(c)).ToList();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new();
        }
    }
}
