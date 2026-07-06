using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels;

/// <summary>Root view-model for the SiegeSmith shell. Owns install discovery (remembered path →
/// registry → common paths → user prompt), the open-tank lifecycle, and the top-level command
/// surface. The <see cref="Explorer"/> is created when a tank is opened.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppSettings.Load();

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private string? _installPath;
    public string? InstallPath
    {
        get => _installPath;
        set { if (SetProperty(ref _installPath, value)) OnPropertyChanged(nameof(InstallLabel)); }
    }

    public string InstallLabel => InstallPath is null
        ? "No Dungeon Siege installation detected.\nUse File ▸ Open Tank to load a tank directly, or File ▸ Refresh Install to search again."
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
        RefreshInstallCommand = new RelayCommand(RefreshInstall);
        OpenTankCommand = new RelayCommand(() => { var p = DialogService.OpenTankFile(); if (p is not null) OpenTank(p); });
        CloseTankCommand = new RelayCommand(CloseTank, () => HasTank);
        ExitCommand = new RelayCommand(() => Application.Current?.Shutdown());
        DetectInstall();
    }

    /// <summary>Silent detection used at startup and by File ▸ Refresh Install.</summary>
    public void DetectInstall()
    {
        // Remembered path wins if it still looks valid.
        if (_settings.InstallPath is { } saved && DsInstallLocator.IsInstall(saved))
        {
            SetInstall(saved, remember: false);
            return;
        }
        // Otherwise probe env var, registry (GOG / retail / Steam), then common paths.
        var found = DsInstallLocator.Locate();
        if (found is not null)
        {
            SetInstall(found, remember: true);
            return;
        }
        InstallPath = null;
        Tanks.Clear();
        StatusText = "No Dungeon Siege installation detected.";
    }

    private void RefreshInstall()
    {
        // A manual refresh re-checks everything and, if still missing, offers the picker.
        _settings.InstallPath = null;
        DetectInstall();
        PromptForInstallIfMissing();
    }

    private void SetInstall(string path, bool remember)
    {
        InstallPath = path;
        if (remember)
        {
            _settings.InstallPath = path;
            _settings.Save();
        }
        Tanks.Clear();
        foreach (var t in DsInstallLocator.FindTanks(path))
            Tanks.Add(new TankListItem(t));
        StatusText = $"Installation: {path} — {Tanks.Count} tank(s)";
    }

    /// <summary>Called once the window is shown: if no install was found, ask the user to point
    /// us at one and remember their choice. Kept out of the constructor so the modal dialog
    /// appears over a visible window rather than during XAML load.</summary>
    public void PromptForInstallIfMissing()
    {
        if (InstallPath is not null) return;

        var choice = MessageBox.Show(
            "SiegeSmith couldn't find your Dungeon Siege installation automatically.\n\n" +
            "Would you like to locate it now? (You can also open individual tanks via File ▸ Open Tank.)",
            "Dungeon Siege not found", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes)
        {
            StatusText = "No installation set — open tanks manually via File ▸ Open Tank.";
            return;
        }

        var folder = DialogService.PickFolder("Select your Dungeon Siege installation folder");
        if (folder is null) return;

        if (DsInstallLocator.IsInstall(folder))
        {
            SetInstall(folder, remember: true);
        }
        else
        {
            StatusText = $"That folder has no Resources subfolder — not a Dungeon Siege install: {folder}";
            MessageBox.Show(
                "That folder doesn't contain a 'Resources' subfolder, so it doesn't look like a " +
                "Dungeon Siege installation.\n\nYou can try again from File ▸ Refresh Install.",
                "Not a Dungeon Siege install", MessageBoxButton.OK, MessageBoxImage.Warning);
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
