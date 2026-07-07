using System.Windows;

namespace SiegeSmith;

/// <summary>Interaction logic for BuildTankWindow.xaml — the folder-to-tank packager dialog.</summary>
public partial class BuildTankWindow : Window
{
    public BuildTankWindow() => InitializeComponent();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
