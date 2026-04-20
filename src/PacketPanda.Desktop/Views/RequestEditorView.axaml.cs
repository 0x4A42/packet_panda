using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    public RequestEditorView()
    {
        InitializeComponent();

        this.WhenActivated(d =>
        {
            if (DataContext is not RequestEditorViewModel vm) return;

            d(vm.UrlTokensChanged.Subscribe(tokens =>
                ApplyTokens(this.FindControl<TextBlock>("UrlVariablePreview")!, tokens)));

            d(vm.BodyTokensChanged.Subscribe(tokens =>
                ApplyTokens(this.FindControl<TextBlock>("BodyVariablePreview")!, tokens)));
        });
    }

    private static void ApplyTokens(TextBlock block, IReadOnlyList<TemplateToken> tokens)
    {
        block.Inlines!.Clear();
        bool hasVars = tokens.Any(t => t.IsVariable);
        if (!hasVars)
        {
            block.IsVisible = false;
            return;
        }
        block.IsVisible = true;
        foreach (var token in tokens)
        {
            if (token.IsVariable)
            {
                block.Inlines.Add(new Run(token.Text)
                {
                    Foreground = token.IsDefined ? Brushes.SteelBlue : Brushes.IndianRed,
                    FontWeight = FontWeight.SemiBold,
                });
            }
            else
            {
                block.Inlines.Add(new Run(token.Text));
            }
        }
    }
}
