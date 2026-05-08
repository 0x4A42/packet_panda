using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class DocsSidebarViewModel : ReactiveObject
{
    private readonly Subject<string> _docOpenRequested = new();

    public DocsSidebarViewModel(string docsDirectory)
    {
        DocItems = new ObservableCollection<DocItemViewModel>();

        OpenDocCommand = ReactiveCommand.Create<DocItemViewModel>(item =>
            _docOpenRequested.OnNext(item.FilePath));

        if (!Directory.Exists(docsDirectory)) return;

        foreach (var path in Directory.GetFiles(docsDirectory, "*.md").Order())
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var displayName = stem.Length > 0 ? char.ToUpper(stem[0]) + stem[1..] : stem;
            DocItems.Add(new DocItemViewModel(displayName, path));
        }
    }

    public ObservableCollection<DocItemViewModel> DocItems { get; }
    public ReactiveCommand<DocItemViewModel, Unit> OpenDocCommand { get; }
    public IObservable<string> DocOpenRequested => _docOpenRequested;
}
