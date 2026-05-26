using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace PacketPanda.Desktop.Views;

// ReSharper disable once PartialTypeWithSinglePart
public partial class ImportCurlView : UserControl
{
    private readonly TextBox _curlTextBox;

    public string? Result { get; private set; }

    public ImportCurlView()
    {
        InitializeComponent();
        _curlTextBox = this.FindControl<TextBox>("CurlTextBox")
            ?? throw new InvalidOperationException("CurlTextBox not found in ImportCurlView");
        var importButton = this.FindControl<Button>("ImportButton")
            ?? throw new InvalidOperationException("ImportButton not found in ImportCurlView");
        var cancelButton = this.FindControl<Button>("CancelButton")
            ?? throw new InvalidOperationException("CancelButton not found in ImportCurlView");

        importButton.Click += (_, _) => { Result = _curlTextBox.Text; CloseParent(); };
        cancelButton.Click += (_, _) => CloseParent();
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
