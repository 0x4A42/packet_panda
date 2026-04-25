using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Tests.ViewModels;

public class TabViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);
    private readonly Mock<IRequestFileStore> _store = new(MockBehavior.Strict);
    private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IDialogService> _dialog = new(MockBehavior.Strict);

    private RequestEditorViewModel NewEditor() =>
        new(_sender.Object, _store.Object, _picker.Object,
            new VariableResolver(),
            new BehaviorSubject<Environment?>(null));

    [Fact]
    public void Title_ForFreshTab_IsUntitled()
    {
        var tab = new TabViewModel(NewEditor(), _dialog.Object);

        tab.Title.Should().Be("Untitled");
    }

    [Fact]
    public void Title_WhenEditorHasFilePath_UsesFileNameWithoutExtension()
    {
        var editor = NewEditor();
        editor.CurrentFilePath = "/tmp/login.toml";

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.Title.Should().Be("login");
    }

    [Fact]
    public void Title_WhenDirty_IncludesDirtyIndicator()
    {
        var editor = NewEditor();
        editor.CurrentFilePath = "/tmp/login.toml";
        editor.Url = "https://example.com"; // makes it dirty

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.Title.Should().Be("login ●");
    }

    [Fact]
    public void FilePath_ForwardsFromEditor()
    {
        var editor = NewEditor();
        editor.CollectionFilePath = "/tmp/x.toml";

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.FilePath.Should().Be("/tmp/x.toml");
    }

    [Fact]
    public void IsDirty_ForwardsFromEditor()
    {
        var editor = NewEditor();
        editor.Url = "https://example.com";

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task CloseCommand_WhenClean_DoesNotPromptAndSignalsClose()
    {
        var tab = new TabViewModel(NewEditor(), _dialog.Object);
        var observed = false;
        using var _ = tab.CloseRequested.Subscribe(_ => observed = true);

        await tab.CloseCommand.Execute();

        observed.Should().BeTrue();
        _dialog.Verify(d => d.ConfirmDiscardAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CloseCommand_WhenDirtyAndUserConfirms_SignalsClose()
    {
        var editor = NewEditor();
        editor.Url = "https://example.com";
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(true);

        var tab = new TabViewModel(editor, _dialog.Object);
        var observed = false;
        using var _ = tab.CloseRequested.Subscribe(_ => observed = true);

        await tab.CloseCommand.Execute();

        observed.Should().BeTrue();
        _dialog.Verify(d => d.ConfirmDiscardAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CloseCommand_WhenDirtyAndUserCancels_DoesNotSignalClose()
    {
        var editor = NewEditor();
        editor.Url = "https://example.com";
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(false);

        var tab = new TabViewModel(editor, _dialog.Object);
        var observed = false;
        using var _ = tab.CloseRequested.Subscribe(_ => observed = true);

        await tab.CloseCommand.Execute();

        observed.Should().BeFalse();
    }
}
