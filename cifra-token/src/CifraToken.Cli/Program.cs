using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CifraToken.Core;

const string Uso = """
CifraToken, linha de comando. Para a janela com as telas, abra CifraToken.exe (Linux: ./CifraToken), na mesma pasta.

Uso:
  cifratoken-cli cifrar -i ARQUIVO -o SAIDA.p7m (-r CERT.cer | -t IMPRESSAO_DIGITAL | --do-token)... [--oaep]
  cifratoken-cli decifrar -i ARQUIVO.p7m -o SAIDA [--pfx MEU.pfx | --pkcs11 [BIBLIOTECA]]
  cifratoken-cli cifrar-senha -i ARQUIVO -o SAIDA.enc
  cifratoken-cli decifrar-senha -i ARQUIVO.enc -o SAIDA
  cifratoken-cli assinar -i ARQUIVO [-o ARQUIVO.p7s] (--pfx MEU.pfx | --pkcs11 [BIBLIOTECA] | -t IMPRESSAO_DIGITAL)
                    [--politica AD-RB|AD-RT|politica.der] [--act URL] [--act-politica OID]
  cifratoken-cli assinar-pdf -i DOC.pdf [-o DOC-assinado.pdf] (--pfx|--pkcs11|-t ...) [--politica AD-RB|AD-RT] [--act URL]
                    [--estampa visivel|nenhuma] [--logo LOGO.png] [--pagina N|ultima]
                    [--posicao inferior-direito|inferior-esquerdo|superior-direito|superior-esquerdo|centro]
                    [--motivo TEXTO] [--local TEXTO]
  cifratoken-cli verificar -i ARQUIVO [-s ARQUIVO.p7s]   (um PDF sem .p7s ao lado: confere as assinaturas internas)
  cifratoken-cli politica AD-RB|AD-RT|politica.der
  cifratoken-cli destinatarios -i ARQUIVO.p7m
  cifratoken-cli listar [--pkcs11 [BIBLIOTECA]]

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
  assinar-pdf: assinatura PAdES dentro do PDF (políticas PAdES v1.3 da ICP-Brasil) com estampa visível e
         logo opcional (PNG/JPEG). Com --act, a estampa é redonda, "CARIMBADO E ASSINADO"; sem, é o quadro
         de assinatura. --estampa nenhuma assina sem estampa.
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
                var certs = token.Certificados();
                // sem destinatário que confira pelo emissor e nº de série, tenta as chaves de cifrar do token
                var candidatos = certs.Where(c => destinos.Any(d => d.Corresponde(c.Certificado))).ToList();
                if (candidatos.Count == 0) candidatos = certs.Where(c => CmsCifra.PermiteCifrar(c.Certificado)).ToList();
                if (candidatos.Count == 0) throw new CryptographicException("Nenhum certificado do token pode decifrar este arquivo.");
                Console.Error.WriteLine($"Certificado: {CmsCifra.Nome(candidatos[0].Certificado)} (token {candidatos[0].Token})");
                if (token.AvisoPin(candidatos[0]) is { } aviso) Console.Error.WriteLine(aviso);
                var pin = Environment.GetEnvironmentVariable("CIFRATOKEN_PIN") ?? LerSenha("PIN do token: ");
                var pares = new List<(X509Certificate2, RSA)>();
                try
                {
                    foreach (var c in candidatos) pares.Add((c.Certificado, token.ChavePrivada(c, pin)));
                    claro = CmsCifra.DecifrarComChaves(p7m, pares);
                }
                finally
                {
                    foreach (var (_, k) in pares) k.Dispose();
                }
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
        case "assinar" or "assinar-pdf":
        {
            var pdf = args[0] == "assinar-pdf";
            var entrada = Obrigatoria("-i");
            var saida = Opcao("-o") ?? (pdf ? Path.ChangeExtension(entrada, null) + "-assinado.pdf" : entrada + ".p7s");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var nomePolitica = Opcao("--politica");
            if (pdf && nomePolitica is "AD-RB" or "AD-RT") nomePolitica = "PAdES " + nomePolitica;
            var pol = nomePolitica is { } np ? await PoliticaIcp.Resolver(np, http) : null;
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

                var dados = File.ReadAllBytes(entrada);
                byte[] resultado;
                if (pdf)
                {
                    var estilo = (Opcao("--estampa") ?? "visivel") switch
                    {
                        "visivel" or "visível" or "carimbo" or "assinatura" => EstiloEstampa.Visivel,
                        "nenhuma" => EstiloEstampa.Nenhuma,
                        var e => throw new ArgumentException($"Estampa desconhecida: {e}. Use visivel ou nenhuma."),
                    };
                    var posicao = (Opcao("--posicao") ?? "inferior-direito") switch
                    {
                        "inferior-direito" => PosicaoEstampa.InferiorDireito, "inferior-esquerdo" => PosicaoEstampa.InferiorEsquerdo,
                        "superior-direito" => PosicaoEstampa.SuperiorDireito, "superior-esquerdo" => PosicaoEstampa.SuperiorEsquerdo,
                        "centro" => PosicaoEstampa.Centro,
                        var e => throw new ArgumentException($"Posição desconhecida: {e}."),
                    };
                    var pagina = Opcao("--pagina") is { } pg && pg != "ultima" ? int.Parse(pg) : 0;
                    var logo = Opcao("--logo") is { } lg ? File.ReadAllBytes(lg) : null;
                    resultado = await AssinaturaPdf.Assinar(dados, cert, chave, pol, act, http,
                        new Estampa(estilo, pagina, posicao, logo, Opcao("--motivo"), Opcao("--local")), cadeia, Opcao("--act-politica"));
                }
                else resultado = await AssinaturaCades.Assinar(dados, cert, chave, pol, act, http, cadeia, Opcao("--act-politica"));
                File.WriteAllBytes(saida, resultado);
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
            var bytes = File.ReadAllBytes(entrada);
            if (Opcao("-s") is null && AssinaturaPdf.EhPdf(bytes) && !File.Exists(entrada + ".p7s"))
            {
                var todas = AssinaturaPdf.Verificar(bytes);
                if (todas.Count == 0) { Console.WriteLine("O PDF não tem assinatura digital."); return 1; }
                var ok = true;
                foreach (var a in todas)
                {
                    var s = a.Assinatura;
                    Console.WriteLine($"Assinatura {a.Numero}: " + (s.Integra ? "ÍNTEGRA" : "INVÁLIDA" + (s.Erro is null ? "" : ": " + s.Erro)));
                    Console.WriteLine($"  Signatário: {s.Signatario}");
                    if (s.Politica is not null) Console.WriteLine($"  Política: {PoliticaIcp.Descrever(s.Politica)}");
                    if (s.Carimbo is not null)
                        Console.WriteLine($"  Carimbo do tempo: {s.Carimbo:dd/MM/yyyy HH:mm:ss} UTC por {s.Act} ({(s.CarimboValido == true ? "íntegro" : "INVÁLIDO")})");
                    Console.WriteLine(a.CobreDocumentoInteiro ? "  Cobre o documento inteiro." : "  Atenção: o PDF recebeu alterações depois desta assinatura.");
                    if (s.CadeiaStatus is not null) Console.WriteLine("  " + s.CadeiaStatus);
                    ok &= s.Integra && s.CarimboValido != false;
                }
                return ok ? 0 : 1;
            }
            var v = AssinaturaCades.Verificar(bytes, File.ReadAllBytes(Opcao("-s") ?? entrada + ".p7s"));
            Console.WriteLine(v.Integra ? "ASSINATURA ÍNTEGRA" : "ASSINATURA INVÁLIDA" + (v.Erro is null ? "" : ": " + v.Erro));
            Console.WriteLine($"Signatário: {v.Signatario}");
            if (v.Politica is not null) Console.WriteLine($"Política: {PoliticaIcp.Descrever(v.Politica)}");
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
