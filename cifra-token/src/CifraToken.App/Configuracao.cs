using System.Text.Json;
using CifraToken.Core;

namespace CifraToken.App;

enum ModoCredencial { Token, Windows, Pfx }

/// <summary>Preferências do usuário, gravadas em preferencias.json na pasta de configuração do usuário.</summary>
sealed class Configuracao
{
    public static readonly string Pasta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CifraToken");
    static readonly string Arquivo = Path.Combine(Pasta, "preferencias.json");

    public ModoCredencial Modo { get; set; } = ModoCredencial.Token;
    public string Driver { get; set; } = TokenPkcs11.PrimeiraBibliotecaInstalada() ?? "";
    public string ArquivoPfx { get; set; } = "";
    public bool EncerrarSessaoAposUso { get; set; } = true;
    public bool UsarOaep { get; set; }
    public string PastaContatos { get; set; } = Contatos.PastaPadrao;
    /// <summary>"" (sem política), "AD-RB" ou "AD-RT" (DOC-ICP-15.03), ou o caminho de um .der.</summary>
    public string PoliticaAssinatura { get; set; } = "AD-RB";
    public bool UsarCarimbo { get; set; }
    public string UrlAct { get; set; } = CarimboTempo.UrlActDefesa;
    /// <summary>OID da política da ACT a pedir no carimbo (vazio: a ACT usa a padrão).</summary>
    public string PoliticaAct { get; set; } = "";
    /// <summary>PDF: assinar dentro do arquivo (PAdES) com estampa visível.</summary>
    public bool AssinarDentroDoPdf { get; set; } = true;
    /// <summary>Estampa visível no PDF: redonda "CARIMBADO E ASSINADO" com carimbo do tempo, quadro de assinatura sem.</summary>
    public bool MostrarEstampa { get; set; } = true;
    public PosicaoEstampa PosicaoEstampa { get; set; } = PosicaoEstampa.InferiorDireito;
    /// <summary>0 = última página, 1 = primeira.</summary>
    public int PaginaEstampa { get; set; }
    public string ArquivoLogo { get; set; } = "";
    /// <summary>Última pasta escolhida para gravar os resultados.</summary>
    public string UltimaPasta { get; set; } = "";

    public static Configuracao Ler()
    {
        try
        {
            if (File.Exists(Arquivo))
                return JsonSerializer.Deserialize<Configuracao>(File.ReadAllText(Arquivo)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Gravar()
    {
        Directory.CreateDirectory(Pasta);
        File.WriteAllText(Arquivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public Configuracao Copia() => (Configuracao)MemberwiseClone();
}
