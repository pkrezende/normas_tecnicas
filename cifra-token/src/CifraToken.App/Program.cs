namespace CifraToken.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new JanelaPrincipal(args.FirstOrDefault()));
    }
}
