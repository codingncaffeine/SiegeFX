using System.Windows;
using System.Windows.Controls;
using SiegeSmith.ViewModels;

namespace SiegeSmith;

/// <summary>Interaction logic for MainWindow.xaml — the SiegeSmith shell window.</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>WPF's TreeView.SelectedItem is read-only, so we push tree selection into the
    /// explorer view-model here. (Search-result selection binds directly via the ListBox.)</summary>
    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel { Explorer: { } explorer })
            explorer.SelectedNode = e.NewValue as TankNodeViewModel;
    }
}
