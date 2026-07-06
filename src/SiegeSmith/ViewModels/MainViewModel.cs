using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels;

/// <summary>Root view-model for the SiegeSmith shell. Owns install discovery and the
/// top-level command surface; the panel view-models (tank explorer, inspectors,
/// preview) hang off this as later phases land.</summary>
public sealed class MainViewModel : ObservableObject
{
    private string _statusText = "Ready";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private string? _installPath;
    public string? InstallPath
    {
        get => _installPath;
        set { if (SetProperty(ref _installPath, value)) OnPropertyChanged(nameof(InstallLabel)); }
    }

    /// <summary>Human-readable install summary shown on the welcome pane.</summary>
    public string InstallLabel => InstallPath is null
        ? "No Dungeon Siege installation detected.\nSet the SIEGEFX_DS1 environment variable, or open a tank manually."
        : $"Installation:  {InstallPath}";

    public ObservableCollection<TankListItem> Tanks { get; } = new();

    public RelayCommand RefreshInstallCommand { get; }
    public RelayCommand ExitCommand { get; }

    public MainViewModel()
    {
        RefreshInstallCommand = new RelayCommand(DetectInstall);
        ExitCommand = new RelayCommand(() => Application.Current?.Shutdown());
        DetectInstall();
    }

    /// <summary>(Re)locates the DS1 install and refreshes the tank list.</summary>
    public void DetectInstall()
    {
        InstallPath = DsInstallLocator.Locate();
        Tanks.Clear();
        if (InstallPath is not null)
        {
            foreach (var t in DsInstallLocator.FindTanks(InstallPath))
                Tanks.Add(new TankListItem(t));
            StatusText = $"Found installation at {InstallPath} — {Tanks.Count} tank(s)";
        }
        else
        {
            StatusText = "No Dungeon Siege installation detected — set SIEGEFX_DS1 or open a tank manually";
        }
    }
}

/// <summary>A resource tank on disk, shown in the left rail.</summary>
public sealed record TankListItem(string FullPath)
{
    public string Name => Path.GetFileName(FullPath);
}
