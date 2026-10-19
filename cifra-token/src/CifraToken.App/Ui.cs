using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CifraToken.App;

/// <summary>Ícones desenhados como geometria (iguais no Windows e no Linux, sem depender de fontes).</summary>
static class Icones
{
    const string Anel = "M12,2A10,10 0 1 1 12,22A10,10 0 1 1 12,2M12,4A8,8 0 1 0 12,20A8,8 0 1 0 12,4Z";
    public const string Menu = "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z";
    public const string Cadeado = "M6,10H18V21H6Z M8,10V7A4,4 0 0 1 16,7V10H14V7A2,2 0 0 0 10,7V10Z";
    public const string CadeadoAberto = "M6,10H18V21H6Z M8,10V7A4,4 0 0 1 16,7H14A2,2 0 0 0 10,7V10Z";
    public const string Pessoas = "M9,4A3,3 0 1 1 9,10A3,3 0 1 1 9,4M3,19C3,15 6,13 9,13C12,13 15,15 15,19Z M16,6A2.5,2.5 0 1 1 16,11A2.5,2.5 0 1 1 16,6M16,13C19,13 21,15 21,18H16.5C16.5,16 16,14.5 15,13.5Z";
    public const string Engrenagem = Anel0 + " M11,1H13V5H11Z M11,19H13V23H11Z M1,11H5V13H1Z M19,11H23V13H19Z M4.2,5.6L5.6,4.2L8.4,7L7,8.4Z M15.6,17L17,15.6L19.8,18.4L18.4,19.8Z M4.2,18.4L7,15.6L8.4,17L5.6,19.8Z M15.6,7L18.4,4.2L19.8,5.6L17,8.4Z";
    const string Anel0 = "M12,6A6,6 0 1 1 12,18A6,6 0 1 1 12,6M12,9A3,3 0 1 0 12,15A3,3 0 1 0 12,9Z";
    public const string Ajuda = Anel + " M11,16H13V18H11Z M12,6A3.5,3.5 0 0 1 15.5,9.5C15.5,11.5 13,12 13,14H11C11,11 13.5,10.8 13.5,9.5A1.5,1.5 0 0 0 10.5,9.5H8.5A3.5,3.5 0 0 1 12,6Z";
    public const string Info = Anel + " M11,10H13V17H11Z M11,7H13V9H11Z";
    public const string Desligar = "M11,2H13V12H11Z M6.3,5.3L7.7,6.7A7,7 0 1 0 16.3,6.7L17.7,5.3A9,9 0 1 1 6.3,5.3Z";
    public const string Mais = "M11,4H13V11H20V13H13V20H11V13H4V11H11Z";
    public const string Lixeira = "M7,7H17L16,21H8Z M5,4H19V6H5Z M10,2H14V4H10Z";
    public const string Lupa = "M10,3A7,7 0 1 1 10,17A7,7 0 1 1 10,3M10,5A5,5 0 1 0 10,15A5,5 0 1 0 10,5Z M14.5,15.9L15.9,14.5L21,19.6L19.6,21Z";
    public const string Certo = "M9,16.2L4.8,12L3.4,13.4L9,19L21,7L19.6,5.6Z";
    public const string Xis = "M6.4,5L5,6.4L10.6,12L5,17.6L6.4,19L12,13.4L17.6,19L19,17.6L13.4,12L19,6.4L17.6,5L12,10.6Z";
    public const string Anterior = "M15.4,7.4L14,6L8,12L14,18L15.4,16.6L10.8,12Z";
    public const string Proximo = "M8.6,16.6L10,18L16,12L10,6L8.6,7.4L13.2,12Z";
    public const string Exportar = "M5,20H19V18H5Z M11,4H13V12H16L12,16L8,12H11Z";
    public const string Pasta = "M3,5H10L12,7H21V19H3Z";
    public const string Caneta = "M3,17.2V21H6.8L17.8,10L14,6.2Z M20.7,7A1,1 0 0 0 20.7,5.6L18.4,3.3A1,1 0 0 0 17,3.3L15.2,5.1L19,8.9Z M3,22H21V23H3Z";
    public const string Selo = Anel + " M10.5,16.5L6.5,12.5L7.9,11.1L10.5,13.7L16.1,8.1L17.5,9.5Z";
    public const string Token = "M7,3H17V14H7Z M9,14H15V21H9Z M10,5H14V8H10Z";

    public static PathIcon Criar(string dados, double tamanho = 18) => new()
    {
        Data = StreamGeometry.Parse("F0 " + dados), Width = tamanho, Height = tamanho,
    };
}

static class Ui
{
    public static Button Botao(string icone, string dica, Action clique)
    {
        var b = new Button { Content = Icones.Criar(icone, 16), Padding = new Thickness(6), Background = Brushes.Transparent };
        ToolTip.SetTip(b, dica);
        b.Click += (_, _) => clique();
        return b;
    }

    public static Button BotaoTexto(string icone, string texto, Action clique)
    {
        var b = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { Icones.Criar(icone, 14), new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center } },
            },
            MinWidth = 100, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        b.Click += (_, _) => clique();
        return b;
    }

    public static Button BotaoSimples(string texto, Action clique)
    {
        var b = new Button { Content = texto };
        b.Click += (_, _) => clique();
        return b;
    }

    public static TextBlock Rotulo(string texto, bool negrito = false) => new()
    {
        Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2),
        FontWeight = negrito ? FontWeight.SemiBold : FontWeight.Normal,
    };

    public static Border Grupo(string titulo, Control conteudo) => new()
    {
        BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 0),
        Child = new StackPanel { Spacing = 4, Children = { Rotulo(titulo, negrito: true), conteudo } },
    };

    static Window Dono(Visual v) => (Window)TopLevel.GetTopLevel(v)!;

    public static Task Mensagem(Visual v, string texto, string titulo = "CifraToken") =>
        Perguntar(v, texto, titulo, Icones.Info, "Ok");

    /// <summary>Caixa de mensagem com botões; devolve o índice do botão clicado (-1 se fechar).</summary>
    public static async Task<int> Perguntar(Visual v, string texto, string titulo, string icone, params string[] botoes)
    {
        var resultado = -1;
        var janela = new Window
        {
            Title = titulo, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, MaxWidth = 560,
        };
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < botoes.Length; i++)
        {
            var indice = i;
            linha.Children.Add(BotaoSimples(botoes[i], () => { resultado = indice; janela.Close(); }));
        }
        janela.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 12,
                    Children = { Icones.Criar(icone, 28), new SelectableTextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 } },
                },
                linha,
            },
        };
        await janela.ShowDialog(Dono(v));
        return resultado;
    }

    public static async Task<bool> Confirmar(Visual v, string texto, string titulo = "Confirmar") =>
        await Perguntar(v, texto, titulo, Icones.Ajuda, "Sim", "Não") == 0;

    public static async Task<List<string>> AbrirArquivos(Visual v, string titulo, bool varios, params (string Nome, string[] Padroes)[] filtros)
    {
        var arquivos = await Dono(v).StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titulo, AllowMultiple = varios,
            FileTypeFilter = filtros.Select(f => new FilePickerFileType(f.Nome) { Patterns = f.Padroes }).ToList(),
        });
        return arquivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task<string?> SalvarArquivo(Visual v, string titulo, string nomeSugerido, string extensao)
    {
        var arquivo = await Dono(v).StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = titulo, SuggestedFileName = nomeSugerido, DefaultExtension = extensao,
        });
        return arquivo?.TryGetLocalPath();
    }

    public static async Task<string?> EscolherPasta(Visual v, string titulo, string? inicial = null)
    {
        var provedor = Dono(v).StorageProvider;
        var comeco = Directory.Exists(inicial) ? await provedor.TryGetFolderFromPathAsync(inicial!) : null;
        var pastas = await provedor.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = titulo, SuggestedStartLocation = comeco });
        return pastas.FirstOrDefault()?.TryGetLocalPath();
    }
}
