using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;
using Net.Pkcs11Interop.Common;

namespace CifraToken.App;

/// <summary>
/// A credencial criptográfica do usuário (certificado + chave privada usados para decifrar e assinar),
/// conforme as Preferências: token pelo driver PKCS#11, repositório do Windows ou arquivo .pfx.
/// </summary>
sealed class Credencial : IDisposable
{
    readonly Configuracao cfg;
    TokenPkcs11? token;
    readonly List<(X509Certificate2 Cert, RSA Chave)> chaves = new();

    public Credencial(Configuracao cfg) => this.cfg = cfg;

    /// <summary>No modo Windows não há conexão: o middleware do token pede o PIN na hora.</summary>
    public bool PrecisaConectar => cfg.Modo != ModoCredencial.Windows;
    public bool Conectada => !PrecisaConectar || chaves.Count > 0;

    public string Descricao => cfg.Modo == ModoCredencial.Windows
        ? "Repositório do Windows (o middleware do token pede o PIN)"
        : string.Join("; ", chaves.Select(c => CmsCifra.Nome(c.Cert)).Distinct());

    /// <summary>De onde vem a credencial, para mostrar antes de abri-la.</summary>
    public string Origem => cfg.Modo switch
    {
        ModoCredencial.Pfx => "Arquivo " + Path.GetFileName(cfg.ArquivoPfx),
        ModoCredencial.Windows => Descricao,
        _ => "Token, pelo driver " + Path.GetFileName(cfg.Driver),
    };

    public string RotuloSenha => cfg.Modo == ModoCredencial.Pfx ? "Senha do arquivo .pfx:" : "PIN para acesso à credencial:";

    /// <summary>Aviso do token sobre tentativas de PIN restantes, lido sem gastar tentativa.</summary>
    public string? AvisoPin()
    {
        if (cfg.Modo != ModoCredencial.Token || !File.Exists(cfg.Driver)) return null;
        try
        {
            using var t = new TokenPkcs11(cfg.Driver);
            var primeiro = t.Certificados().FirstOrDefault();
            return primeiro is null ? null : t.AvisoPin(primeiro);
        }
        // o aviso é só uma cortesia: se falhar, o Conectar mostra o erro de verdade
        catch (Exception e) when (e is CryptographicException or Pkcs11Exception or UnmanagedException) { return null; }
    }

    public void Conectar(string pin)
    {
        Desconectar();
        switch (cfg.Modo)
        {
            case ModoCredencial.Token:
                if (string.IsNullOrWhiteSpace(cfg.Driver))
                    throw new CryptographicException("Configure o driver do token em Preferências > Credencial.");
                if (!File.Exists(cfg.Driver))
                    throw new CryptographicException($"O arquivo do driver do token não foi encontrado em \"{cfg.Driver}\". Verifique as Preferências.");
                if (string.IsNullOrEmpty(pin)) throw new CryptographicException("Digite o PIN do token.");
                token = new TokenPkcs11(cfg.Driver);
                var certs = token.Certificados();
                if (certs.Count == 0)
                    throw new CryptographicException("A conexão foi feita, mas nenhum certificado foi encontrado. O token está conectado?");
                foreach (var c in certs)
                {
                    // erro de PIN interrompe na hora, para não gastar outras tentativas
                    try { chaves.Add((c.Certificado, token.ChavePrivada(c, pin))); }
                    catch (CryptographicException e) when (e.InnerException is not Pkcs11Exception) { }
                }
                if (chaves.Count == 0)
                    throw new CryptographicException("O token não tem chave privada para os certificados encontrados.");
                break;

            case ModoCredencial.Pfx:
                if (!File.Exists(cfg.ArquivoPfx))
                    throw new CryptographicException($"O arquivo de credencial não foi encontrado em \"{cfg.ArquivoPfx}\". Verifique as Preferências.");
                X509Certificate2Collection colecao;
                try { colecao = X509CertificateLoader.LoadPkcs12CollectionFromFile(cfg.ArquivoPfx, pin, X509KeyStorageFlags.UserKeySet); }
                catch (CryptographicException) { throw new CryptographicException("Senha do .pfx incorreta ou arquivo inválido."); }
                foreach (var c in colecao)
                    if (c.HasPrivateKey && c.GetRSAPrivateKey() is { } rsa) chaves.Add((c, rsa));
                if (chaves.Count == 0)
                    throw new CryptographicException("O .pfx não tem chave privada RSA.");
                break;
        }
    }

    public byte[] Decifrar(byte[] p7m)
    {
        if (cfg.Modo == ModoCredencial.Windows) return CmsCifra.Decifrar(p7m);
        return CmsCifra.DecifrarComChaves(p7m, chaves);
    }

    /// <summary>Certificados que podem assinar (uso de chave digitalSignature ou nonRepudiation) e têm chave acessível.</summary>
    public List<X509Certificate2> CertificadosAssinatura()
    {
        var lista = cfg.Modo switch
        {
            ModoCredencial.Windows => Repositorio.Pessoais().Where(c => c.HasPrivateKey),
            // sem PIN ainda: os certificados do token são públicos e o PIN é pedido ao assinar
            ModoCredencial.Token when chaves.Count == 0 => CertificadosDoToken(),
            _ => chaves.Select(c => c.Cert),
        };
        return lista.Where(PodeAssinar).OrderByDescending(c => c.NotAfter).ToList();
    }

    /// <summary>Chave do certificado conectado; nula no modo Windows (o .NET usa a do repositório e o middleware pede o PIN).</summary>
    public RSA? ChaveDe(X509Certificate2 cert) => cfg.Modo == ModoCredencial.Windows
        ? null
        : chaves.FirstOrDefault(c => c.Cert.Thumbprint == cert.Thumbprint).Chave
          ?? throw new CryptographicException("O token não tem a chave privada do certificado escolhido.");

    List<X509Certificate2>? doToken;

    /// <summary>Certificados lidos do token sem PIN, guardados até a credencial mudar.</summary>
    List<X509Certificate2> CertificadosDoToken()
    {
        if (doToken is not null) return doToken;
        if (!File.Exists(cfg.Driver)) return new();
        try
        {
            using var t = new TokenPkcs11(cfg.Driver);
            return doToken = t.Certificados().Select(c => c.Certificado).ToList();
        }
        catch (Exception e) when (e is CryptographicException or Pkcs11Exception or UnmanagedException
                                      or DllNotFoundException or BadImageFormatException) { return new(); }
    }

    /// <summary>Esquece os certificados lidos do token (por exemplo, depois de trocar de token).</summary>
    public void Reler() => doToken = null;

    static bool PodeAssinar(X509Certificate2 c) => c.Extensions.OfType<X509KeyUsageExtension>()
        .All(k => k.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature) || k.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation));

    /// <summary>Certificados da credencial que podem ser lidos sem PIN (para cadastrar a si mesmo como contato).</summary>
    public List<X509Certificate2> CertificadosPublicos()
    {
        switch (cfg.Modo)
        {
            case ModoCredencial.Token:
                if (!File.Exists(cfg.Driver)) return new();
                using (var t = new TokenPkcs11(cfg.Driver))
                    return t.Certificados().Select(c => c.Certificado).ToList();
            case ModoCredencial.Windows:
                return Repositorio.Pessoais().Where(c => c.HasPrivateKey).ToList();
            default:
                return chaves.Select(c => X509CertificateLoader.LoadCertificate(c.Cert.RawData)).ToList();
        }
    }

    public void Desconectar()
    {
        foreach (var (cert, chave) in chaves)
        {
            chave.Dispose();
            if (cfg.Modo == ModoCredencial.Pfx) cert.Dispose();
        }
        chaves.Clear();
        token?.Dispose();
        token = null;
    }

    public void Dispose() => Desconectar();
}
