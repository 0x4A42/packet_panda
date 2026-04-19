using FluentAssertions;
using PacketPanda.Core.Persistence;

namespace PacketPanda.Tests.Persistence;

public class WorkspaceScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pp-scan-" + Guid.NewGuid().ToString("N"));
    private readonly IWorkspaceScanner _scanner = new WorkspaceScanner();

    public WorkspaceScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Scan_EmptyFolder_ReturnsFolderWithNoChildren()
    {
        var result = await _scanner.ScanAsync(_root);

        result.Path.Should().Be(_root);
        result.Children.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_TomlFilesOnly_ReturnsRequestNodesAlphabetical()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "b.toml"), "");
        await File.WriteAllTextAsync(Path.Combine(_root, "a.toml"), "");

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().HaveCount(2);
        result.Children[0].Should().BeOfType<RequestNode>().Which.Name.Should().Be("a.toml");
        result.Children[1].Should().BeOfType<RequestNode>().Which.Name.Should().Be("b.toml");
    }

    [Fact]
    public async Task Scan_NonTomlFiles_AreIgnored()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "readme.md"), "");
        await File.WriteAllTextAsync(Path.Combine(_root, "req.toml"), "");

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().ContainSingle()
            .Which.Name.Should().Be("req.toml");
    }

    [Fact]
    public async Task Scan_NestedSubfolders_ProducesRecursiveTree()
    {
        var sub = Path.Combine(_root, "api");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "login.toml"), "");

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().ContainSingle();
        var folder = result.Children[0].Should().BeOfType<FolderNode>().Subject;
        folder.Name.Should().Be("api");
        folder.Children.Should().ContainSingle()
            .Which.Name.Should().Be("login.toml");
    }

    [Fact]
    public async Task Scan_FoldersListedBeforeFiles_AtEachLevel()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "z.toml"), "");
        Directory.CreateDirectory(Path.Combine(_root, "a-folder"));

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().HaveCount(2);
        result.Children[0].Should().BeOfType<FolderNode>();
        result.Children[1].Should().BeOfType<RequestNode>();
    }
}
