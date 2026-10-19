using System.Security.Cryptography.X509Certificates;

namespace CifraToken.Core;

/// <summary>
/// Agenda de contatos: cada contato é o certificado público (.cer) de alguém para quem se cifra.
/// Os arquivos ficam numa pasta comum, para que possam ser copiados ou distribuídos pelo setor.
/// </summary>
public sealed class Contatos
{
    public string Pasta { get; }

    public Contatos(string pasta)
    {
        Pasta = pasta;
        Directory.CreateDirectory(pasta);
    }

    public static string PastaPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CifraToken", "contatos");

    public List<X509Certificate2> Todos()
    {
        var lista = new List<X509Certificate2>();
        foreach (var arquivo in Directory.EnumerateFiles(Pasta, "*.cer"))
        {
            try { lista.Add(CmsCifra.LerCertificado(arquivo)); }
            catch (System.Security.Cryptography.CryptographicException) { /* arquivo inválido: ignorado */ }
        }
        return lista.OrderBy(CmsCifra.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Grava só a parte pública do certificado. Devolve false se o contato já existia.</summary>
    public bool Adicionar(X509Certificate2 cert)
    {
        var caminho = Caminho(cert);
        if (File.Exists(caminho)) return false;
        File.WriteAllBytes(caminho, cert.Export(X509ContentType.Cert));
        return true;
    }

    public void Remover(X509Certificate2 cert) => File.Delete(Caminho(cert));

    string Caminho(X509Certificate2 cert)
    {
        var nome = string.Concat(CmsCifra.Nome(cert).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Pasta, $"{nome} - {cert.Thumbprint[..8]}.cer");
    }
}
