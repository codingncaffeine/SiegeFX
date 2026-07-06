using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels;

/// <summary>Root view-model for the SiegeSmith shell. Owns install discovery, the open-tank
/// lifecycle, and the top-level command surface. The <see cref="Explorer"/> is created when a
/// tank is opened and drives the browser, inspector, and workspace panes.</summary>
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

    public string InstallLabel => InstallPath is null
        ? "No Dungeon Siege installation detected.\nSet the SIEGEFX_DS1 environment variable, or open a tank manually."
        : $"Installation:  {InstallPath}";

    public ObservableCollection<TankListItem> Tanks { get; } = new();

    private TankListItem? _selectedTank;
    public TankListItem? SelectedTank
    {
        get => _selectedTank;
        set { if (SetProperty(ref _selectedTank, value) && value is not null) OpenTank(value.FullPath); }
    }

    private TankExplorerViewModel? _explorer;
    public TankExplorerViewModel? Explorer
    {
        get => _explorer;
        private set { if (SetProperty(ref _explorer, value)) OnPropertyChanged(nameof(HasTank)); }
    }

    public bool HasTank => Explorer is not null;

    public RelayCommand RefreshInstallCommand { get; }
    public RelayCommand OpenTankCommand { get; }
    public RelayCommand CloseTankCommand { get; }
    public RelayCommand ExitCommand { get; }

    public MainViewModel()
    {
        RefreshInstallCommand = new RelayCommand(DetectInstall);
        OpenTankCommand = new RelayCommand(() => { var p = DialogService.OpenTankFile(); if (p is not null) OpenTank(p); });
        CloseTankCommand = new RelayCommand(CloseTank, () => HasTank);
        ExitCommand = new RelayCommand(() => Application.Current?.Shutdown());
        DetectInstall();
    }

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

    private void OpenTank(string path)
    {
        try
        {
            var doc = TankDocument.Open(path);
            var explorer = new TankExplorerViewModel(doc);
            explorer.Status += s => StatusText = s;
            Explorer?.Dispose();
            Explorer = explorer;
            CloseTankCommand.RaiseCanExecuteChanged();
            StatusText = $"Opened {doc.Name} — {doc.FileCount:N0} files, {Format.Bytes(doc.SizeBytes)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void CloseTank()
    {
        Explorer?.Dispose();
        Explorer = null;
        CloseTankCommand.RaiseCanExecuteChanged();
        StatusText = "Closed tank";
    }
}

/// <summary>A resource tank on disk, shown in the left-rail tank list.</summary>
public sealed record TankListItem(string FullPath)
{
    public string Name => Path.GetFileName(FullPath);
}
