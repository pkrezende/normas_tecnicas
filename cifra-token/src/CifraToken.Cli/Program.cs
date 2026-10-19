using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;

const string Uso = """
Uso:
  cifratoken cifrar -i ARQUIVO -o SAIDA.p7m (-r CERT.cer | -t IMPRESSAO_DIGITAL | --do-token)... [--oaep]
  cifratoken decifrar -i ARQUIVO.p7m -o SAIDA [--pfx MEU.pfx | --pkcs11 [BIBLIOTECA]]
  cifratoken cifrar-senha -i ARQUIVO -o SAIDA.enc
  cifratoken decifrar-senha -i ARQUIVO.enc -o SAIDA
  cifratoken assinar -i ARQUIVO [-o ARQUIVO.p7s] (--pfx MEU.pfx | --pkcs11 [BIBLIOTECA] | -t IMPRESSAO_DIGITAL)
                    [--politica AD-RB|AD-RT|politica.der] [--act URL] [--act-politica OID]
  cifratoken verificar -i ARQUIVO [-s ARQUIVO.p7s]
  cifratoken politica AD-RB|AD-RT|politica.der
  cifratoken destinatarios -i ARQUIVO.p7m
  cifratoken listar [--pkcs11 [BIBLIOTECA]]

  -r  certificado do destinatário em arquivo (.cer, .crt ou .pem); repita para vários
  -t  certificado do destinatário no repositório do Windows, pela impressão digital (veja "listar")
  --do-token  inclui como destinatário o(s) certificado(s) do token (lidos por PKCS#11)
  --pfx  decifra com um A1 (.pfx/.p12); a senha vem da variável CIFRATOKEN_SENHA ou é pedida
  --pkcs11  fala direto com o token pela biblioteca do fabricante (Dexon: DXSafePKCS11.dll);
         sem caminho, procura as bibliotecas conhecidas. O PIN vem de CIFRATOKEN_PIN ou é pedido.
  cifrar-senha/decifrar-senha: formato .enc do Quimera e do openssl enc (AES-256-CBC, PBKDF2-SHA512,
         1.000.000 de iterações); a frase-senha vem de CIFRATOKEN_SENHA ou é pedida
  assinar: assinatura destacada CAdES (.p7s). --politica inclui a Política de Assinatura ICP-Brasil
         (DOC-ICP-15.03), baixada do ITI; AD-RT exige --act (carimbo do tempo RFC 3161).
         ACT Defesa: https://sct.acdefesa.mil.br/timestamp (só nas redes cadastradas do MD e das Forças).
  Sem --pfx, a chave é procurada no repositório "Pessoal" (onde o middleware do token
  registra o certificado) e o middleware pede o PIN.
""";

if (args.Length == 0) { Console.Error.WriteLine(Uso); return 2; }

try
{
    string? Opcao(string nome)
    {
        var i = Array.IndexOf(args, nome);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    IEnumerable<string> Opcoes(string nome)
    {
        for (var i = 1; i < args.Length - 1; i++)
            if (args[i] == nome) yield return args[i + 1];
    }
    string Obrigatoria(string nome) => Opcao(nome) ?? throw new ArgumentException($"Falta {nome}.");
    TokenPkcs11 AbrirToken(string opcao)
    {
        var caminho = Opcao(opcao) is { } c && !c.StartsWith('-') ? c : TokenPkcs11.PrimeiraBibliotecaInstalada()
            ?? throw new ArgumentException("Nenhuma biblioteca PKCS#11 conhecida encontrada. Informe o caminho após " + opcao + ".");
        return new TokenPkcs11(caminho);
    }

    switch (args[0])
    {
        case "cifrar":
        {
            var certs = Opcoes("-r").Select(CmsCifra.LerCertificado)
                .Concat(Opcoes("-t").Select(Repositorio.PorImpressao)).ToList();
            if (args.Contains("--do-token"))
            {
                using var token = AbrirToken("--do-token");
                certs.AddRange(token.Certificados().Select(c => c.Certificado).Where(CmsCifra.PermiteCifrar));
            }
            foreach (var c in certs.Where(c => !CmsCifra.PermiteCifrar(c)))
                Console.Error.WriteLine($"Aviso: \"{CmsCifra.Nome(c)}\" não tem keyEncipherment no Key Usage.");
            var p7m = CmsCifra.Cifrar(File.ReadAllBytes(Obrigatoria("-i")), certs,
                args.Contains("--oaep") ? PreenchimentoRsa.OaepSha1 : PreenchimentoRsa.Pkcs1);
            File.WriteAllBytes(Obrigatoria("-o"), p7m);
            Console.WriteLine($"Cifrado para {certs.Count} destinatário(s): {Obrigatoria("-o")}");
            return 0;
        }
        case "decifrar":
        {
            X509Certificate2Collection? chaves = null;
            if (Opcao("--pfx") is { } pfx)
            {
                var senha = Environment.GetEnvironmentVariable("CIFRATOKEN_SENHA") ?? LerSenha("Senha do .pfx: ");
                chaves = X509CertificateLoader.LoadPkcs12CollectionFromFile(pfx, senha, X509KeyStorageFlags.UserKeySet);
            }
            var p7m = File.ReadAllBytes(Obrigatoria("-i"));
            byte[] claro;
            if (args.Contains("--pkcs11"))
            {
                using var token = AbrirToken("--pkcs11");
                var destinos = CmsCifra.Destinatarios(p7m);
                var alvo = token.Certificados().FirstOrDefault(c => destinos.Any(d => d.Corresponde(c.Certificado)))
                    ?? throw new CryptographicException("Nenhum certificado do token é destinatário deste arquivo.");
                Console.Error.WriteLine($"Certificado: {CmsCifra.Nome(alvo.Certificado)} (token {alvo.Token})");
                if (token.AvisoPin(alvo) is { } aviso) Console.Error.WriteLine(aviso);
                var pin = Environment.GetEnvironmentVariable("CIFRATOKEN_PIN") ?? LerSenha("PIN do token: ");
                using var chave = token.ChavePrivada(alvo, pin);
                claro = CmsCifra.Decifrar(p7m, alvo.Certificado, chave);
            }
            else
                claro = CmsCifra.Decifrar(p7m, chaves);
            File.WriteAllBytes(Obrigatoria("-o"), claro);
            Console.WriteLine($"Decifrado: {Obrigatoria("-o")}");
            return 0;
        }
        case "cifrar-senha":
        {
            var senha = Environment.GetEnvironmentVariable("CIFRATOKEN_SENHA") ?? LerSenha("Frase-senha: ");
            File.WriteAllBytes(Obrigatoria("-o"), SenhaOpenSsl.Cifrar(File.ReadAllBytes(Obrigatoria("-i")), senha));
            Console.WriteLine($"Cifrado com senha: {Obrigatoria("-o")}");
            return 0;
        }
        case "decifrar-senha":
        {
            var senha = Environment.GetEnvironmentVariable("CIFRATOKEN_SENHA") ?? LerSenha("Frase-senha: ");
            File.WriteAllBytes(Obrigatoria("-o"), SenhaOpenSsl.Decifrar(File.ReadAllBytes(Obrigatoria("-i")), senha));
            Console.WriteLine($"Decifrado: {Obrigatoria("-o")}");
            return 0;
        }
        case "politica":
        {
            var pol = await PoliticaIcp.Resolver(args.Length > 1 ? args[1] : "AD-RB", new HttpClient());
            Console.WriteLine($"{pol.Nome}\nOID: {pol.Oid}\nURL: {pol.Url}\nResumo ({pol.AlgoritmoResumo.Name}): {Convert.ToHexString(pol.Resumo)}");
            return 0;
        }
        case "assinar":
        {
            var entrada = Obrigatoria("-i");
            var saida = Opcao("-o") ?? entrada + ".p7s";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var pol = Opcao("--politica") is { } np ? await PoliticaIcp.Resolver(np, http) : null;
            var act = Opcao("--act");
            X509Certificate2 cert;
            RSA? chave = null;
            TokenPkcs11? token = null;
            var cadeia = new List<X509Certificate2>();
            try
            {
                if (Opcao("--pfx") is { } pfx)
                {
                    var senha = Environment.GetEnvironmentVariable("CIFRATOKEN_SENHA") ?? LerSenha("Senha do .pfx: ");
                    var colecao = X509CertificateLoader.LoadPkcs12CollectionFromFile(pfx, senha, X509KeyStorageFlags.UserKeySet);
                    cert = colecao.Cast<X509Certificate2>().First(c => c.HasPrivateKey);
                    cadeia.AddRange(colecao.Cast<X509Certificate2>());
                }
                else if (args.Contains("--pkcs11"))
                {
                    token = AbrirToken("--pkcs11");
                    var certs = token.Certificados();
                    var alvo = certs.FirstOrDefault(c => c.Certificado.Extensions.OfType<X509KeyUsageExtension>()
                                   .All(k => k.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature) || k.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation)))
                               ?? throw new CryptographicException("Nenhum certificado de assinatura no token.");
                    Console.Error.WriteLine($"Certificado: {CmsCifra.Nome(alvo.Certificado)} (token {alvo.Token})");
                    if (token.AvisoPin(alvo) is { } aviso) Console.Error.WriteLine(aviso);
                    var pin = Environment.GetEnvironmentVariable("CIFRATOKEN_PIN") ?? LerSenha("PIN do token: ");
                    chave = token.ChavePrivada(alvo, pin);
                    cert = alvo.Certificado;
                    cadeia.AddRange(certs.Select(c => c.Certificado));
                }
                else cert = Repositorio.PorImpressao(Obrigatoria("-t"));

                var p7s = await AssinaturaCades.Assinar(File.ReadAllBytes(entrada), cert, chave, pol, act, http, cadeia, Opcao("--act-politica"));
                File.WriteAllBytes(saida, p7s);
                Console.WriteLine($"Assinado: {saida}" + (pol is null ? "" : $" (política {pol.Oid})") + (act is null ? "" : " com carimbo do tempo"));
                return 0;
            }
            finally
            {
                chave?.Dispose();
                token?.Dispose();
            }
        }
        case "verificar":
        {
            var entrada = Obrigatoria("-i");
            var v = AssinaturaCades.Verificar(File.ReadAllBytes(entrada), File.ReadAllBytes(Opcao("-s") ?? entrada + ".p7s"));
            Console.WriteLine(v.Integra ? "ASSINATURA ÍNTEGRA" : "ASSINATURA INVÁLIDA" + (v.Erro is null ? "" : ": " + v.Erro));
            Console.WriteLine($"Signatário: {v.Signatario}");
            if (v.Politica is not null) Console.WriteLine($"Política: {v.Politica}");
            if (v.Carimbo is not null)
                Console.WriteLine($"Carimbo do tempo: {v.Carimbo:dd/MM/yyyy HH:mm:ss} UTC por {v.Act} ({(v.CarimboValido == true ? "íntegro" : "INVÁLIDO")})");
            if (v.CadeiaStatus is not null) Console.WriteLine(v.CadeiaStatus);
            return v.Integra && v.CarimboValido != false ? 0 : 1;
        }
        case "destinatarios":
            foreach (var d in CmsCifra.Destinatarios(File.ReadAllBytes(Obrigatoria("-i"))))
                Console.WriteLine(d);
            return 0;
        case "listar" when args.Contains("--pkcs11"):
        {
            using var token = AbrirToken("--pkcs11");
            Console.WriteLine($"Biblioteca: {token.Caminho}");
            foreach (var c in token.Certificados())
                Console.WriteLine($"[{c.Token}] {CmsCifra.Nome(c.Certificado)}  (válido até {c.Certificado.NotAfter:dd/MM/yyyy})"
                    + (CmsCifra.PermiteCifrar(c.Certificado) ? "" : "  [não permite cifrar]"));
            return 0;
        }
        case "listar":
            foreach (var c in Repositorio.Pessoais())
                Console.WriteLine($"{c.Thumbprint}  {CmsCifra.Nome(c)}  (válido até {c.NotAfter:dd/MM/yyyy})"
                    + (c.HasPrivateKey ? "  [chave privada]" : "")
                    + (CmsCifra.PermiteCifrar(c) ? "" : "  [não permite cifrar]"));
            return 0;
        default:
            Console.Error.WriteLine(Uso);
            return 2;
    }
}
catch (Exception e) when (e is CryptographicException or ArgumentException or IOException
                            or Net.Pkcs11Interop.Common.Pkcs11Exception or Net.Pkcs11Interop.Common.UnmanagedException
                            or HttpRequestException or InvalidOperationException)
{
    Console.Error.WriteLine("Erro: " + e.Message);
    return 1;
}

static string LerSenha(string pergunta)
{
    Console.Error.Write(pergunta);
    var senha = new System.Text.StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(true);
        if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace) { if (senha.Length > 0) senha.Length--; }
        else senha.Append(k.KeyChar);
    }
    Console.Error.WriteLine();
    return senha.ToString();
}
