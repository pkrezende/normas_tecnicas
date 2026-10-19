namespace CifraToken.App;

/// <summary>Caixa para digitar PIN ou senha sem mostrar os caracteres.</summary>
sealed class PedirSenha : Form
{
    readonly TextBox caixa = new() { UseSystemPasswordChar = true, Width = 260 };

    PedirSenha(string pergunta, string? aviso)
    {
        Text = "CifraToken";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var painel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        if (aviso is not null)
            painel.Controls.Add(new Label { Text = aviso, AutoSize = true, ForeColor = Color.DarkRed, MaximumSize = new Size(300, 0) });
        painel.Controls.Add(new Label { Text = pergunta, AutoSize = true });
        painel.Controls.Add(caixa);
        var botoes = new FlowLayoutPanel { AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        botoes.Controls.Add(ok);
        botoes.Controls.Add(cancelar);
        painel.Controls.Add(botoes);
        Controls.Add(painel);
        AcceptButton = ok;
        CancelButton = cancelar;
    }

    /// <summary>Devolve o texto digitado, ou null se o usuário cancelar.</summary>
    public static string? Mostrar(IWin32Window dono, string pergunta, string? aviso = null)
    {
        using var janela = new PedirSenha(pergunta, aviso);
        return janela.ShowDialog(dono) == DialogResult.OK ? janela.caixa.Text : null;
    }
}
