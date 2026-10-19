using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace CifraToken.Core;

/// <summary>Certificado encontrado no token, com o identificador (CKA_ID) que liga ao par de chaves.</summary>
public sealed record CertificadoNoToken(X509Certificate2 Certificado, byte[] Id, string Token);

/// <summary>
/// Acesso direto ao token pela biblioteca PKCS#11 do fabricante (no Dexon, DXSafePKCS11.dll),
/// sem depender de o certificado estar publicado no repositório do Windows.
/// A chave privada nunca sai do token: só a chave de sessão do arquivo é decifrada lá dentro.
/// </summary>
public sealed class TokenPkcs11 : IDisposable
{
    /// <summary>Bibliotecas PKCS#11 conhecidas (fabricante, caminho), testadas na ordem quando o usuário não escolhe uma.</summary>
    public static readonly (string Fabricante, string Caminho)[] BibliotecasConhecidas =
    {
        ("Dexon DXSafe", @"C:\Windows\System32\DXSafePKCS11.dll"),
        ("G+D SafeSign", @"C:\Windows\System32\aetpkss1.dll"),
        ("SafeNet / Thales eToken", @"C:\Windows\System32\eTPKCS11.dll"),
        ("Watchdata", @"C:\Windows\System32\WDPKCS.dll"),
        ("Watchdata (CCID)", @"C:\Windows\System32\WDICP_P11_CCID_v34.dll"),
        ("Thales / Gemalto IDPrime", @"C:\Program Files\Gemalto\IDGo 800 PKCS11\IDPrimePKCS1164.dll"),
        ("Thales / Gemalto IDPrime", @"C:\Program Files (x86)\Gemalto\IDGo 800 PKCS11\IDPrimePKCS11.dll"),
        ("Gemalto GemSafe", @"C:\Windows\System32\gclib.dll"),
        ("Oberthur / IDEMIA", @"C:\Windows\System32\OcsCryptoki.dll"),
        ("Oberthur / IDEMIA AWP", @"C:\Program Files (x86)\Oberthur Technologies\AWP\DLLs\OcsCryptolib_P11.dll"),
        ("Feitian ePass", @"C:\Windows\System32\ngp11v211.dll"),
        ("Serpro", @"C:\Windows\System32\SerproPkcs11.dll"),
        ("ACS", @"C:\Windows\System32\acospkcs11.dll"),
        ("OpenSC", @"C:\Program Files\OpenSC Project\OpenSC\pkcs11\opensc-pkcs11.dll"),
        ("OpenSC", @"C:\Windows\System32\opensc-pkcs11.dll"),
        ("OpenSC", "/usr/lib/x86_64-linux-gnu/opensc-pkcs11.so"),
        ("SoftHSM (testes)", "/usr/lib/softhsm/libsofthsm2.so"),
    };

    /// <summary>
    /// Drivers instalados neste computador: os conhecidos e qualquer outra DLL com "pkcs11" ou "p11" no nome
    /// na pasta do sistema. Num aplicativo de 32 bits o Windows mostra o SysWOW64 no lugar do System32.
    /// </summary>
    public static List<(string Fabricante, string Caminho)> BibliotecasInstaladas()
    {
        var achadas = BibliotecasConhecidas.Where(b => File.Exists(b.Caminho))
            .GroupBy(b => b.Caminho, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var sistema = Environment.GetFolderPath(Environment.SpecialFolder.System);
                foreach (var dll in Directory.EnumerateFiles(sistema, "*.dll"))
                {
                    var nome = Path.GetFileName(dll).ToLowerInvariant();
                    if ((nome.Contains("pkcs11") || nome.Contains("p11") || nome.Contains("cryptoki"))
                        && !achadas.Any(a => a.Caminho.Equals(dll, StringComparison.OrdinalIgnoreCase)))
                        achadas.Add(("Outro", dll));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return achadas;
    }

    public static string? PrimeiraBibliotecaInstalada() => BibliotecasInstaladas().Select(b => b.Caminho).FirstOrDefault();

    static readonly Pkcs11InteropFactories Fabricas = new();

    static TokenPkcs11()
    {
        // O Pkcs11Interop procura "libdl", que o glibc 2.34+ só instala como libdl.so.2 (no Windows não se aplica).
        if (OperatingSystem.IsLinux())
            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(Pkcs11InteropFactories).Assembly,
                (nome, assembly, caminhos) =>
                    nome == "libdl" && System.Runtime.InteropServices.NativeLibrary.TryLoad("libdl.so.2", out var h) ? h : IntPtr.Zero);
    }
    readonly IPkcs11Library biblioteca;
    readonly List<ISession> sessoes = new();
    bool descartado;

    public string Caminho { get; }

    // Um driver é inicializado uma vez por processo (C_Initialize) e fica carregado até o programa fechar.
    // Abrir de novo daria CKR_CRYPTOKI_ALREADY_INITIALIZED, o C_Finalize de uma instância derrubaria as sessões
    // da outra, e há drivers que respondem CKR_GENERAL_ERROR a um novo C_Initialize depois do C_Finalize.
    static readonly Dictionary<string, IPkcs11Library> Abertas = new(StringComparer.OrdinalIgnoreCase);

    public TokenPkcs11(string caminhoBiblioteca)
    {
        Caminho = caminhoBiblioteca;
        lock (Abertas)
        {
            if (!Abertas.TryGetValue(caminhoBiblioteca, out var aberta))
                Abertas[caminhoBiblioteca] = aberta = Carregar(caminhoBiblioteca);
            biblioteca = aberta;
        }
    }

    /// <summary>
    /// C_Initialize com travas do sistema (multithread); alguns drivers recusam esses argumentos
    /// (CKR_ARGUMENTS_BAD ou CKR_CANT_LOCK) e só aceitam C_Initialize(NULL), o modo de uma thread.
    /// </summary>
    static IPkcs11Library Carregar(string caminho)
    {
        try
        {
            return Fabricas.Pkcs11LibraryFactory.LoadPkcs11Library(Fabricas, caminho, AppType.MultiThreaded);
        }
        catch (Pkcs11Exception e) when (e.RV is CKR.CKR_ARGUMENTS_BAD or CKR.CKR_CANT_LOCK or CKR.CKR_NEED_TO_CREATE_THREADS)
        {
            try { return Fabricas.Pkcs11LibraryFactory.LoadPkcs11Library(Fabricas, caminho, AppType.SingleThreaded); }
            catch (Pkcs11Exception e2) { throw new CryptographicException(Mensagem(e2.RV, "inicializar o driver do token"), e2); }
        }
        catch (Pkcs11Exception e) { throw new CryptographicException(Mensagem(e.RV, "inicializar o driver do token"), e); }
    }

    /// <summary>Tokens conectados e inicializados, na ordem dos slots.</summary>
    public List<ISlot> Tokens()
    {
        List<ISlot> slots;
        try { slots = biblioteca.GetSlotList(SlotsType.WithTokenPresent); }
        catch (Pkcs11Exception e) { throw new CryptographicException(Mensagem(e.RV, "listar os tokens conectados"), e); }
        return slots.Where(s =>
        {
            try { return s.GetTokenInfo().TokenFlags.TokenInitialized; }
            catch (Pkcs11Exception) { return false; } // leitor com cartão não reconhecido
        }).ToList();
    }

    /// <summary>Lê os certificados de todos os tokens conectados. Na maioria dos tokens não pede PIN.</summary>
    public List<CertificadoNoToken> Certificados()
    {
        try { return LerCertificados(); }
        catch (Pkcs11Exception e) { throw new CryptographicException(Mensagem(e.RV, "ler os certificados do token"), e); }
    }

    List<CertificadoNoToken> LerCertificados()
    {
        var lista = new List<CertificadoNoToken>();
        foreach (var slot in Tokens())
        {
            var rotulo = slot.GetTokenInfo().Label.Trim();
            using var sessao = slot.OpenSession(SessionType.ReadOnly);
            foreach (var obj in sessao.FindAllObjects(new List<IObjectAttribute>
                     {
                         Fabricas.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
                         Fabricas.ObjectAttributeFactory.Create(CKA.CKA_CERTIFICATE_TYPE, CKC.CKC_X_509),
                     }))
            {
                var attrs = sessao.GetAttributeValue(obj, new List<CKA> { CKA.CKA_VALUE, CKA.CKA_ID });
                try
                {
                    var cert = X509CertificateLoader.LoadCertificate(attrs[0].GetValueAsByteArray());
                    if (cert.GetRSAPublicKey() is not null)
                        lista.Add(new CertificadoNoToken(cert, attrs[1].GetValueAsByteArray() ?? Array.Empty<byte>(), rotulo));
                }
                catch (CryptographicException) { /* objeto que não é um certificado X.509 válido */ }
            }
        }
        return lista;
    }

    /// <summary>
    /// Aviso do próprio token sobre tentativas de PIN restantes (CKF_USER_PIN_COUNT_LOW / FINAL_TRY), se houver.
    /// </summary>
    public string? AvisoPin(CertificadoNoToken alvo)
    {
        var info = SlotDo(alvo).GetTokenInfo().TokenFlags;
        if (info.UserPinLocked) return "O PIN deste token está BLOQUEADO. Use o Token Admin com o PUK para desbloquear.";
        if (info.UserPinFinalTry) return "ATENÇÃO: resta só UMA tentativa de PIN. Se errar, o token será bloqueado.";
        if (info.UserPinCountLow) return "Atenção: já houve PIN errado neste token. Poucas tentativas restam.";
        return null;
    }

    /// <summary>Abre sessão com PIN e devolve a chave privada do certificado, como objeto RSA que decifra dentro do token.</summary>
    public RSA ChavePrivada(CertificadoNoToken alvo, string pin)
    {
        if (string.IsNullOrEmpty(pin)) throw new CryptographicException("Digite o PIN do token.");
        var slot = SlotDo(alvo);
        var sessao = AbrirSessao(slot, SessionType.ReadOnly);
        try
        {
            sessao.Login(CKU.CKU_USER, pin);
        }
        catch (Pkcs11Exception e) when (e.RV == CKR.CKR_USER_ALREADY_LOGGED_IN) { }
        catch (Pkcs11Exception e) when (e.RV is CKR.CKR_ARGUMENTS_BAD or CKR.CKR_SESSION_READ_ONLY)
        {
            // há drivers que só aceitam o login numa sessão de leitura e escrita
            sessao = AbrirSessao(slot, SessionType.ReadWrite);
            try { sessao.Login(CKU.CKU_USER, pin); }
            catch (Pkcs11Exception e2) when (e2.RV == CKR.CKR_USER_ALREADY_LOGGED_IN) { }
            catch (Pkcs11Exception e2) { throw new CryptographicException(Mensagem(e2.RV, "entrar com o PIN"), e2); }
        }
        catch (Pkcs11Exception e)
        {
            throw new CryptographicException(Mensagem(e.RV, "entrar com o PIN"), e);
        }

        var busca = new List<IObjectAttribute>
        {
            Fabricas.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
            Fabricas.ObjectAttributeFactory.Create(CKA.CKA_KEY_TYPE, CKK.CKK_RSA),
        };
        if (alvo.Id.Length > 0)
            busca.Add(Fabricas.ObjectAttributeFactory.Create(CKA.CKA_ID, alvo.Id));
        List<IObjectHandle> chaves;
        try { chaves = sessao.FindAllObjects(busca); }
        catch (Pkcs11Exception e) { throw new CryptographicException(Mensagem(e.RV, "procurar a chave privada"), e); }
        if (chaves.Count == 0 && alvo.Id.Length > 0)
        {
            // alguns drivers gravam o CKA_ID da chave diferente do certificado: usa a única chave RSA, se houver só uma
            try { chaves = sessao.FindAllObjects(busca.Take(2).ToList()); }
            catch (Pkcs11Exception e) { throw new CryptographicException(Mensagem(e.RV, "procurar a chave privada"), e); }
            if (chaves.Count > 1) chaves.Clear();
        }
        if (chaves.Count == 0)
            throw new CryptographicException($"O token não tem a chave privada do certificado \"{CmsCifra.Nome(alvo.Certificado)}\".");
        return new RsaNoToken(sessao, chaves[0], alvo.Certificado.GetRSAPublicKey()!.KeySize);
    }

    ISession AbrirSessao(ISlot slot, SessionType tipo)
    {
        try
        {
            var sessao = slot.OpenSession(tipo);
            sessoes.Add(sessao);
            return sessao;
        }
        catch (Pkcs11Exception e) { throw new CryptographicException(Mensagem(e.RV, "abrir a sessão no token"), e); }
    }

    ISlot SlotDo(CertificadoNoToken alvo) =>
        Tokens().FirstOrDefault(s => s.GetTokenInfo().Label.Trim() == alvo.Token)
        ?? throw new CryptographicException($"O token \"{alvo.Token}\" não está mais conectado.");

    /// <summary>Mensagem com a etapa em que o token recusou, para facilitar o diagnóstico.</summary>
    public static string Mensagem(CKR rv, string etapa) => rv switch
    {
        CKR.CKR_ARGUMENTS_BAD or CKR.CKR_FUNCTION_FAILED or CKR.CKR_GENERAL_ERROR =>
            $"O token recusou a operação ao {etapa} ({rv}). Confira se o driver escolhido em Preferências é o do seu token " +
            "e se o middleware do fabricante está atualizado.",
        _ => Mensagem(rv),
    };

    public static string Mensagem(CKR rv) => rv switch
    {
        CKR.CKR_PIN_INCORRECT => "PIN incorreto. Cuidado: algumas tentativas erradas bloqueiam o token.",
        CKR.CKR_PIN_LOCKED => "PIN bloqueado. Use o Token Admin com o PUK para desbloquear.",
        CKR.CKR_PIN_LEN_RANGE => "O PIN tem um tamanho que o token não aceita.",
        CKR.CKR_TOKEN_NOT_PRESENT or CKR.CKR_DEVICE_REMOVED => "O token foi desconectado.",
        CKR.CKR_MECHANISM_INVALID => "O token não suporta este preenchimento RSA (tente sem OAEP).",
        CKR.CKR_ENCRYPTED_DATA_INVALID or CKR.CKR_ENCRYPTED_DATA_LEN_RANGE =>
            "O token recusou a chave cifrada: o arquivo pode ter sido cifrado para outro certificado.",
        _ => $"Erro do token: {rv}.",
    };

    public void Dispose()
    {
        if (descartado) return;
        descartado = true;
        foreach (var s in sessoes)
        {
            try { s.Logout(); } catch (Pkcs11Exception) { }
            try { s.Dispose(); } catch (Pkcs11Exception) { }
        }
        // a biblioteca continua carregada (ver Abertas); só as sessões desta instância são encerradas
    }

    /// <summary>RSA cuja operação de decifrar é feita pelo token (C_Decrypt).</summary>
    sealed class RsaNoToken : RSA
    {
        readonly ISession sessao;
        readonly IObjectHandle chave;

        public RsaNoToken(ISession sessao, IObjectHandle chave, int tamanho)
        {
            this.sessao = sessao;
            this.chave = chave;
            LegalKeySizesValue = new[] { new KeySizes(tamanho, tamanho, 0) };
            KeySizeValue = tamanho;
        }

        public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding)
        {
            IMechanism mecanismo;
            if (padding == RSAEncryptionPadding.Pkcs1)
                mecanismo = Fabricas.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
            else if (padding == RSAEncryptionPadding.OaepSHA1)
                mecanismo = Fabricas.MechanismFactory.Create(CKM.CKM_RSA_PKCS_OAEP,
                    Fabricas.MechanismParamsFactory.CreateCkRsaPkcsOaepParams(
                        ConvertUtils.UInt64FromCKM(CKM.CKM_SHA_1), ConvertUtils.UInt64FromCKG(CKG.CKG_MGF1_SHA1),
                        ConvertUtils.UInt64FromUInt32(CKZ.CKZ_DATA_SPECIFIED), null));
            else if (padding == RSAEncryptionPadding.OaepSHA256)
                mecanismo = Fabricas.MechanismFactory.Create(CKM.CKM_RSA_PKCS_OAEP,
                    Fabricas.MechanismParamsFactory.CreateCkRsaPkcsOaepParams(
                        ConvertUtils.UInt64FromCKM(CKM.CKM_SHA256), ConvertUtils.UInt64FromCKG(CKG.CKG_MGF1_SHA256),
                        ConvertUtils.UInt64FromUInt32(CKZ.CKZ_DATA_SPECIFIED), null));
            else
                throw new CryptographicException($"Preenchimento RSA não suportado: {padding}.");

            try
            {
                using (mecanismo) return sessao.Decrypt(mecanismo, chave, data);
            }
            catch (Pkcs11Exception e)
            {
                throw new CryptographicException(Mensagem(e.RV, "decifrar"), e);
            }
        }

        /// <summary>Assina o resumo dentro do token (C_Sign com CKM_RSA_PKCS sobre o DigestInfo, PKCS#1 v1.5).</summary>
        public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            if (padding != RSASignaturePadding.Pkcs1)
                throw new CryptographicException("Só a assinatura RSA PKCS#1 v1.5 é suportada pelo token.");
            byte[] prefixo = hashAlgorithm.Name switch
            {
                "SHA256" => Convert.FromHexString("3031300d060960864801650304020105000420"),
                "SHA384" => Convert.FromHexString("3041300d060960864801650304020205000430"),
                "SHA512" => Convert.FromHexString("3051300d060960864801650304020305000440"),
                _ => throw new CryptographicException($"Resumo não suportado: {hashAlgorithm.Name}."),
            };
            try
            {
                using var mecanismo = Fabricas.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
                return sessao.Sign(mecanismo, chave, prefixo.Concat(hash).ToArray());
            }
            catch (Pkcs11Exception e)
            {
                throw new CryptographicException(Mensagem(e.RV, "assinar"), e);
            }
        }

        public override RSAParameters ExportParameters(bool includePrivateParameters) =>
            throw new CryptographicException("A chave privada não pode sair do token.");

        public override void ImportParameters(RSAParameters parameters) => throw new NotSupportedException();
    }
}
