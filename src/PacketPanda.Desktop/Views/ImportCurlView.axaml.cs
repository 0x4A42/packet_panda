using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace PacketPanda.Desktop.Views;

public partial class ImportCurlView : UserControl
{
    private readonly TextBox _curlTextBox;

    public string? Result { get; private set; }

    public ImportCurlView()
    {
        InitializeComponent();
        _curlTextBox = this.FindControl<TextBox>("CurlTextBox")!;
        this.FindControl<Button>("ImportButton")!.Click += (_, _) => { Result = _curlTextBox.Text; CloseParent(); };
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => CloseParent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            Result = _curlTextBox.Text;
            CloseParent();
            e.Handled = true;
        }
    }

    private void CloseParent() => (VisualRoot as Window)?.Close();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
