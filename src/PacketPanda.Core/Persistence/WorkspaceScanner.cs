using Tomlyn;
using Tomlyn.Model;

namespace PacketPanda.Core.Persistence;

public sealed class WorkspaceScanner : IWorkspaceScanner
{
    public Task<FolderNode> ScanAsync(string rootPath)
    {
        return Task.Run(() => ScanFolder(rootPath));
    }

    private static FolderNode ScanFolder(string path)
    {
        var folders = Directory.GetDirectories(path)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(ScanFolder)
            .Cast<CollectionNode>();

        var files = Directory.GetFiles(path, "*.toml")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(BuildRequestNode)
            .Cast<CollectionNode>();

        var children = folders.Concat(files).ToList();
        return new FolderNode(Path.GetFileName(path), path, children);
    }

    private static RequestNode BuildRequestNode(string path)
    {
        var name = Path.GetFileName(path);
        string? method = null;
        try
        {
            var text = File.ReadAllText(path);
            var table = Toml.ToModel(text);
            if (table.TryGetValue("method", out var v) && v is string m)
                method = m.ToUpperInvariant();
        }
        catch { }
        return new RequestNode(name, path, method);
    }
}
