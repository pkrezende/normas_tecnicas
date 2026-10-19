using CifraToken.Core;

namespace CifraToken.App;

/// <summary>Estado compartilhado pelas telas: preferências, credencial e agenda de contatos.</summary>
sealed class Ambiente : IDisposable
{
    public Configuracao Cfg { get; private set; } = Configuracao.Ler();
    public Credencial Credencial { get; private set; }
    public Contatos Agenda { get; private set; }

    /// <summary>Avisa as telas quando a credencial é conectada ou desconectada, ou as preferências mudam.</summary>
    public event Action? Mudou;

    public Ambiente()
    {
        Credencial = new Credencial(Cfg);
        Agenda = new Contatos(Cfg.PastaContatos);
    }

    public void TrocarConfiguracao(Configuracao nova)
    {
        nova.Gravar();
        Credencial.Dispose();
        Cfg = nova;
        Credencial = new Credencial(Cfg);
        Agenda = new Contatos(Cfg.PastaContatos);
        Avisar();
    }

    /// <summary>Encerra a sessão do token se a preferência mandar.</summary>
    public void DepoisDeUsar()
    {
        if (Cfg.EncerrarSessaoAposUso && Credencial.PrecisaConectar && Credencial.Conectada)
        {
            Credencial.Desconectar();
            Avisar();
        }
    }

    public void Avisar() => Mudou?.Invoke();

    public void Dispose() => Credencial.Dispose();
}
