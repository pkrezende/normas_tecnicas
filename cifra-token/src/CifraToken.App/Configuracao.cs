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
