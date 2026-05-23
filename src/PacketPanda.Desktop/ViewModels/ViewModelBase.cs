using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
}

public enum SidebarTab { Requests, Environments, History, Docs }
