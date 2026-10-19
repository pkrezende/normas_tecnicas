using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CifraToken.App;

/// <summary>
/// Molde das telas Cifrar e Decifrar: lista de arquivos à esquerda (adicionar, remover ou arrastar),
/// painel de opções à direita, mensagem do resultado e os botões Ok e Cancelar embaixo.
/// </summary>
abstract class PaginaArquivos : UserControl
{
    protected readonly Ambiente Amb;
    protected readonly ObservableCollection<string> Arquivos = new();
    readonly ListBox lista;
    protected readonly ContentControl Direita = new() { Margin = new Thickness(8) };
    readonly TextBlock mensagem = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly PathIcon iconeMensagem = Icones.Criar(Icones.Info, 16);
    readonly Border barraMensagem;
    readonly Button ok;

    protected abstract (string Nome, string[] Padroes)[] Filtros { get; }
    protected abstract void ArquivosMudaram();
    protected abstract Task Executar();

    protected PaginaArquivos(Ambiente amb)
    {
        Amb = amb;
        lista = new ListBox
        {
            ItemsSource = Arquivos, SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<string>((caminho, _) =>
            {
                var item = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Path.GetFileName(caminho), FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = Path.GetDirectoryName(caminho), FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                };
                ToolTip.SetTip(item, caminho);
                return item;
            }),
        };
        lista.KeyDown += (_, e) => { if (e.Key == Key.Delete) RemoverSelecionados(); };

        var cab = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 4),
            Children =
            {
                new TextBlock { Text = "Arquivos:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                Ui.Botao(Icones.Mais, "Adicionar arquivos", () => _ = Escolher()),
                Ui.Botao(Icones.Lixeira, "Remover os arquivos selecionados", RemoverSelecionados),
            },
        };
        var esquerda = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(cab, Dock.Top);
        esquerda.Children.Add(cab);
        esquerda.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)), BorderThickness = new Thickness(1),
            Child = lista,
        });
        var dica = new TextBlock
        {
            Text = "Arraste arquivos para cá ou use o botão +", Opacity = 0.6, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
        };
        DockPanel.SetDock(dica, Dock.Bottom);
        esquerda.Children.Insert(1, dica);

        barraMensagem = new Border
        {
            Padding = new Thickness(8, 6), Margin = new Thickness(8, 0, 8, 4), IsVisible = false, CornerRadius = new CornerRadius(4),
            Child = MontarLinhaMensagem(),
        };
        var lado = new DockPanel();
        DockPanel.SetDock(barraMensagem, Dock.Bottom);
        lado.Children.Add(barraMensagem);
        lado.Children.Add(new ScrollViewer { Content = Direita });

        var corpo = new Grid { ColumnDefinitions = new ColumnDefinitions("320,4,*") };
        corpo.Children.Add(esquerda);
        var divisor = new GridSplitter { Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)) };
        Grid.SetColumn(divisor, 1);
        corpo.Children.Add(divisor);
        Grid.SetColumn(lado, 2);
        corpo.Children.Add(lado);

        ok = Ui.BotaoTexto(Icones.Certo, "Ok", () => _ = ExecutarComEspera());
        var rodape = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8),
            Children = { ok, Ui.BotaoTexto(Icones.Xis, "Cancelar", Limpar) },
        };
        var raiz = new DockPanel();
        DockPanel.SetDock(rodape, Dock.Bottom);
        raiz.Children.Add(rodape);
        raiz.Children.Add(corpo);
        Content = raiz;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.Data.GetFiles() is { } itens) Adicionar(itens.Select(i => i.TryGetLocalPath()).OfType<string>());
        });
    }

    DockPanel MontarLinhaMensagem()
    {
        iconeMensagem.Margin = new Thickness(0, 0, 8, 0);
        iconeMensagem.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(iconeMensagem, Dock.Left);
        return new DockPanel { Children = { iconeMensagem, mensagem } };
    }

    public void Adicionar(IEnumerable<string> caminhos)
    {
        foreach (var c in caminhos.Where(File.Exists))
            if (!Arquivos.Contains(c)) Arquivos.Add(c);
        ArquivosMudaram();
    }

    void RemoverSelecionados()
    {
        foreach (var item in (lista.SelectedItems?.Cast<string>() ?? Enumerable.Empty<string>()).ToList()) Arquivos.Remove(item);
        ArquivosMudaram();
    }

    async Task Escolher() => Adicionar(await Ui.AbrirArquivos(this, "Adicionar arquivos", true, Filtros));

    async Task ExecutarComEspera()
    {
        ok.IsEnabled = false;
        Cursor = new Cursor(StandardCursorType.Wait);
        try { await Executar(); }
        finally
        {
            ok.IsEnabled = true;
            Cursor = Cursor.Default;
        }
    }

    protected virtual void Limpar()
    {
        Arquivos.Clear();
        barraMensagem.IsVisible = false;
        ArquivosMudaram();
    }

    protected void Informar(string texto, bool erro = false)
    {
        mensagem.Text = texto;
        iconeMensagem.Data = StreamGeometry.Parse("F0 " + (erro ? Icones.Xis : Icones.Info));
        barraMensagem.Background = new SolidColorBrush(erro ? Color.FromArgb(50, 220, 50, 50) : Color.FromArgb(40, 0, 120, 215));
        barraMensagem.IsVisible = true;
    }

    /// <summary>Quem escolhe a pasta de saída (trocável para testes da tela sem janela de diálogo).</summary>
    internal static Func<Visual, string, string?, Task<string?>> SeletorDePasta = Ui.EscolherPasta;

    /// <summary>
    /// Pergunta em que pasta gravar os resultados, começando pela última usada (ou a do primeiro arquivo).
    /// Devolve null se o usuário cancelar.
    /// </summary>
    protected async Task<string?> EscolherPastaSaida(string titulo)
    {
        var inicial = Directory.Exists(Amb.Cfg.UltimaPasta) ? Amb.Cfg.UltimaPasta : Path.GetDirectoryName(Arquivos.FirstOrDefault() ?? "");
        var pasta = await SeletorDePasta(this, titulo, inicial);
        if (pasta is null) { Informar("Operação cancelada: nenhuma pasta escolhida para gravar os resultados."); return null; }
        Amb.Cfg.UltimaPasta = pasta;
        try { Amb.Cfg.Gravar(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return pasta;
    }

    /// <summary>
    /// Se a credencial (token ou .pfx) ainda não estiver aberta, pede o PIN ou a senha numa janela própria e conecta.
    /// No modo Windows não pede nada: o middleware do token pede o PIN na hora. Devolve false se o usuário cancelar.
    /// </summary>
    protected async Task<bool> AbrirCredencial(string oQue)
    {
        var cred = Amb.Credencial;
        if (cred.Conectada) return true;
        var aviso = await Task.Run(cred.AvisoPin);
        var ok = await JanelaPin.Pedir(this, cred.RotuloSenha.TrimEnd(':'), oQue, aviso, async pin =>
        {
            try
            {
                await Task.Run(() => cred.Conectar(pin));
                return null;
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or IOException
                                          or Net.Pkcs11Interop.Common.Pkcs11Exception or Net.Pkcs11Interop.Common.UnmanagedException
                                          or DllNotFoundException or BadImageFormatException)
            {
                return PainelCredencial.TextoErro(e);
            }
        });
        Amb.Avisar();
        return ok;
    }

    /// <summary>Pergunta antes de sobrescrever: true grava, false pula, null para tudo.</summary>
    protected async Task<bool?> PodeGravar(string destino)
    {
        if (!File.Exists(destino)) return true;
        return await Ui.Perguntar(this, $"O arquivo já existe:\n{destino}\n\nDeseja sobrescrever?", "Arquivo existente",
                Icones.Ajuda, "Sobrescrever", "Pular", "Parar") switch
            { 0 => true, 1 => false, _ => null };
    }

    /// <summary>Lê os primeiros bytes para saber se o arquivo é .enc (senha) ou CMS (destinatários).</summary>
    protected static bool EhArquivoComSenha(string caminho)
    {
        try
        {
            using var f = File.OpenRead(caminho);
            var cab = new byte[8];
            return f.Read(cab, 0, 8) == 8 && System.Text.Encoding.ASCII.GetString(cab) == "Salted__";
        }
        catch (IOException) { return false; }
    }
}
