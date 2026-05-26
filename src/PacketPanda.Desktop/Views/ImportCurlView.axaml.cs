using Avalonia.Controls;
using Avalonia.Input;

namespace PacketPanda.Desktop.Views;

public partial class ImportCurlView : UserControl
{
    public string? Result { get; private set; }

    public ImportCurlView()
    {
        InitializeComponent();
        ImportButton.Click += (_, _) => { Result = CurlTextBox.Text; CloseParent(); };
        CancelButton.Click += (_, _) => CloseParent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            Result = CurlTextBox.Text;
            CloseParent();
            e.Handled = true;
        }
    }

    private void CloseParent() => (VisualRoot as Window)?.Close();
}
