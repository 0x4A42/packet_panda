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
            .Select(p => (CollectionNode)new RequestNode(Path.GetFileName(p), p));

        var children = folders.Concat(files).ToList();
        return new FolderNode(Path.GetFileName(path), path, children);
    }
}
