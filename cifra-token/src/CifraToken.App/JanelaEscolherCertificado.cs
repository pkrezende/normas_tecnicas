using System.Security.Cryptography.X509Certificates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CifraToken.Core;

namespace CifraToken.App;

/// <summary>Lista os certificados do token (pelo driver PKCS#11) e, no Windows, os dos repositórios do usuário.</summary>
sealed class JanelaEscolherCertificado : Window
{
    readonly Configuracao cfg;
    readonly StackPanel itens = new() { Spacing = 2 };
    readonly TextBlock aviso = new() { TextWrapping = TextWrapping.Wrap };

    public JanelaEscolherCertificado(Configuracao cfg)
    {
        this.cfg = cfg;
        Title = "Escolher certificados";
        Width = 720;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var botoes = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                Ui.BotaoSimples("Atualizar", Carregar),
                Ui.BotaoTexto(Icones.Certo, "Adicionar", () => Close(itens.Children.OfType<CheckBox>()
                    .Where(c => c.IsChecked == true).Select(c => (X509Certificate2)c.Tag!).ToList())),
                Ui.BotaoTexto(Icones.Xis, "Cancelar", () => Close(null)),
            },
        };
        var raiz = new DockPanel { Margin = new Thickness(12) };
        var topo = Ui.Rotulo("Conecte o token antes de abrir esta lista ou clique em Atualizar. Marque os certificados que deseja adicionar aos contatos.");
        DockPanel.SetDock(topo, Dock.Top);
        DockPanel.SetDock(botoes, Dock.Bottom);
        DockPanel.SetDock(aviso, Dock.Bottom);
        raiz.Children.Add(topo);
        raiz.Children.Add(botoes);
        raiz.Children.Add(aviso);
        raiz.Children.Add(new ScrollViewer { Content = itens, Margin = new Thickness(0, 8) });
        Content = raiz;
        Opened += (_, _) => Carregar();
    }

    void Carregar()
    {
        itens.Children.Clear();
        aviso.Text = "";
        var vistos = new HashSet<string>();
        void Item(X509Certificate2 c, string origem)
        {
            if (!vistos.Add(c.Thumbprint)) return;
            itens.Children.Add(new CheckBox
            {
                Tag = c,
                Content = $"{CmsCifra.Nome(c)}  ·  {origem}  ·  válido até {c.NotAfter:dd/MM/yyyy}" +
                          (CmsCifra.PermiteCifrar(c) ? "" : "  ·  não permite cifrar"),
            });
        }

        if (File.Exists(cfg.Driver))
        {
            try
            {
                using var token = new TokenPkcs11(cfg.Driver);
                foreach (var c in token.Certificados()) Item(c.Certificado, $"token {c.Token}");
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or Net.Pkcs11Interop.Common.Pkcs11Exception
                                          or Net.Pkcs11Interop.Common.UnmanagedException or DllNotFoundException or BadImageFormatException)
            {
                aviso.Text = $"Não consegui ler o token pelo driver {Path.GetFileName(cfg.Driver)}: {e.Message}";
            }
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var c in Repositorio.Pessoais()) Item(c, c.HasPrivateKey ? "Windows: Pessoal (com chave)" : "Windows: Pessoal");
            foreach (var c in Repositorio.OutrasPessoas()) Item(c, "Windows: Outras Pessoas");
        }
        if (itens.Children.Count == 0 && aviso.Text.Length == 0)
            aviso.Text = "Nenhum certificado encontrado. Confira se o token está conectado e o driver em Preferências.";
    }
}
