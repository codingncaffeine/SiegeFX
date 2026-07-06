using System.Windows;
using System.Windows.Controls;
using SiegeSmith.ViewModels;

namespace SiegeSmith;

/// <summary>Interaction logic for MainWindow.xaml — the SiegeSmith shell window.</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Once the window is up, offer the locate-install prompt if detection came up empty.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.PromptForInstallIfMissing();
    }

    /// <summary>WPF's TreeView.SelectedItem is read-only, so we push tree selection into the
    /// explorer view-model here. (Search-result selection binds directly via the ListBox.)</summary>
    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel { Explorer: { } explorer })
            explorer.SelectedNode = e.NewValue as TankNodeViewModel;
    }
}
