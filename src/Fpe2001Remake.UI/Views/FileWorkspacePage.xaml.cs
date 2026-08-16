using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fpe2001Remake.UI.ViewModels.Modules;

namespace Fpe2001Remake.UI.Views;

public partial class FileWorkspacePage : UserControl
{
    public FileWorkspacePage()
    {
        InitializeComponent();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is FileWorkspaceViewModel vm && e.NewValue is DirectoryNode node)
        {
            vm.SelectedDirectory = node;
        }
    }

    private void Files_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FileWorkspaceViewModel vm && vm.OpenInEditorCommand.CanExecute(null))
        {
            vm.OpenInEditorCommand.Execute(null);
        }
    }

    private void Hits_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FileWorkspaceViewModel vm && vm.OpenInEditorCommand.CanExecute(null))
        {
            vm.OpenInEditorCommand.Execute(null);
        }
    }
}
