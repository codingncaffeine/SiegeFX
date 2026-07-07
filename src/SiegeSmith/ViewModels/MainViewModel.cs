using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using SiegeFX.Core.Tank;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels;

/// <summary>Root view-model for the SiegeSmith shell. Owns install discovery (remembered path →
/// registry → common paths → user prompt), the open-tank lifecycle, mod-project management, and
/// the top-level command surface.</summary>
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

    // ── mod project ─────────────────────────────────────────────
    private ModProject? _project;
    private string? _projectPath;
    public bool HasProject => _project is not null;
    public string ProjectLabel => _project is null ? "No project open" : $"Project: {_project.Name}  ({_project.SourceFolder})";
    public string? ProjectSourceFolder => _project?.SourceFolder;

    public RelayCommand RefreshInstallCommand { get; }
    public RelayCommand OpenTankCommand { get; }
    public RelayCommand CloseTankCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand BuildInstallCommand { get; }
    public RelayCommand LaunchGameCommand { get; }

    public MainViewModel()
    {
        RefreshInstallCommand = new RelayCommand(RefreshInstall);
        OpenTankCommand = new RelayCommand(() => { var p = DialogService.OpenTankFile(); if (p is not null) OpenTank(p); });
        CloseTankCommand = new RelayCommand(CloseTank, () => HasTank);
        ExitCommand = new RelayCommand(() => Application.Current?.Shutdown());
        NewProjectCommand = new RelayCommand(_ => NewProject());
        OpenProjectCommand = new RelayCommand(_ => OpenProject());
        SaveProjectCommand = new RelayCommand(_ => SaveProject(), _ => HasProject);
        BuildInstallCommand = new RelayCommand(_ => BuildInstall(), _ => HasProject && InstallPath is not null);
        LaunchGameCommand = new RelayCommand(_ => LaunchGame(), _ => InstallPath is not null);
        DetectInstall();
    }

    /// <summary>Silent detection used at startup and by File ▸ Refresh Install.</summary>
    public void DetectInstall()
    {
        if (_settings.InstallPath is { } saved && DsInstallLocator.IsInstall(saved))
        {
            SetInstall(saved, remember: false);
            return;
        }
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
    /// us at one and remember their choice.</summary>
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

    // ── project commands ────────────────────────────────────────
    private void SetProject(ModProject project, string path)
    {
        _project = project;
        _projectPath = path;
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectLabel));
    }

    private void NewProject()
    {
        var folder = DialogService.PickFolder("Select the mod's source folder (loose files)");
        if (folder is null) return;
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        var project = new ModProject { Name = string.IsNullOrWhiteSpace(name) ? "MyMod" : name, SourceFolder = folder };
        var savePath = DialogService.SaveProjectFile(project.Name);
        if (savePath is null) return;
        try
        {
            project.Save(savePath);
            SetProject(project, savePath);
            StatusText = $"Created project {project.Name}";
        }
        catch (Exception ex) { StatusText = "Couldn't save project: " + ex.Message; }
    }

    private void OpenProject()
    {
        var path = DialogService.OpenProjectFile();
        if (path is null) return;
        try
        {
            var project = ModProject.Load(path);
            SetProject(project, path);
            StatusText = $"Opened project {project.Name} ({project.SourceFolder})";
        }
        catch (Exception ex) { StatusText = "Couldn't open project: " + ex.Message; }
    }

    private void SaveProject()
    {
        if (_project is null || _projectPath is null) return;
        try { _project.Save(_projectPath); StatusText = $"Saved project {_project.Name}"; }
        catch (Exception ex) { StatusText = "Save failed: " + ex.Message; }
    }

    private async void BuildInstall()
    {
        if (_project is null) { StatusText = "Open or create a project first."; return; }
        if (InstallPath is null) { StatusText = "No Dungeon Siege install — set one via File ▸ Refresh Install."; return; }
        if (!Directory.Exists(_project.SourceFolder)) { StatusText = "The project's source folder doesn't exist."; return; }

        var dest = Path.Combine(InstallPath, "Resources", _project.Name + ".dsres");
        var confirm = MessageBox.Show(
            $"Build the project and install it as:\n\n{dest}\n\nThis writes into your Dungeon Siege Resources folder " +
            "(delete the file to uninstall). Continue?",
            "Build & Install Mod", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        var project = _project;
        var when = DateTime.UtcNow;
        StatusText = "Building & installing…";
        try
        {
            var (files, bytes) = await Task.Run(() => TankBuilder.BuildFromFolder(
                project.SourceFolder, dest, project.Name, project.Author, project.Description, TankPriority.User, when));
            StatusText = $"Installed {project.Name}.dsres — {files:N0} file(s), {Format.Bytes(bytes)}. Launch to test.";
        }
        catch (Exception ex) { StatusText = "Build & install failed: " + ex.Message; }
    }

    private void LaunchGame()
    {
        if (InstallPath is null) { StatusText = "No Dungeon Siege installation detected."; return; }
        var exe = GameLauncher.FindExecutable(InstallPath);
        if (exe is null) { StatusText = "Couldn't find the Dungeon Siege executable in the install folder."; return; }
        try { GameLauncher.Launch(exe); StatusText = $"Launched {Path.GetFileName(exe)}"; }
        catch (Exception ex) { StatusText = "Launch failed: " + ex.Message; }
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
