using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fpe2001Remake.UI.ViewModels.Modules;

namespace Fpe2001Remake.UI.Views;

public partial class MediaPage : UserControl
{
    public MediaPage()
    {
        InitializeComponent();
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MediaWorkspaceViewModel vm &&
            sender is FrameworkElement fe &&
            fe.DataContext is MediaItemRow row)
        {
            vm.Selected = row;
        }
    }
}
