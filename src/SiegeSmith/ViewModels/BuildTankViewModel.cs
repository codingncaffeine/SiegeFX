using System;
using System.IO;
using System.Threading.Tasks;
using SiegeFX.Core.Tank;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels;

/// <summary>Backs the Build Tank dialog: collects a source folder, output path, and header
/// metadata, then runs <see cref="TankBuilder"/> off the UI thread. Priority defaults to
/// <see cref="TankPriority.User"/>, the correct load-order tier for a mod.</summary>
public sealed class BuildTankViewModel : ObservableObject
{
    private string _sourceFolder = "";
    public string SourceFolder { get => _sourceFolder; set { if (SetProperty(ref _sourceFolder, value)) BuildCommand.RaiseCanExecuteChanged(); } }

    private string _outputPath = "";
    public string OutputPath { get => _outputPath; set { if (SetProperty(ref _outputPath, value)) BuildCommand.RaiseCanExecuteChanged(); } }

    private string _tankTitle = "";
    public string TankTitle { get => _tankTitle; set => SetProperty(ref _tankTitle, value); }

    private string _tankAuthor = "";
    public string TankAuthor { get => _tankAuthor; set => SetProperty(ref _tankAuthor, value); }

    private string _description = "";
    public string Description { get => _description; set => SetProperty(ref _description, value); }

    private string _status = "Pick a source folder and an output tank file, then Build.";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _busy;
    public bool Busy { get => _busy; private set { if (SetProperty(ref _busy, value)) BuildCommand.RaiseCanExecuteChanged(); } }

    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand BuildCommand { get; }

    public BuildTankViewModel()
    {
        BrowseSourceCommand = new RelayCommand(_ =>
        {
            var f = DialogService.PickFolder("Select the mod's source folder");
            if (f is not null) SourceFolder = f;
        });
        BrowseOutputCommand = new RelayCommand(_ =>
        {
            var f = DialogService.SaveTankFile(TankTitle);
            if (f is not null) OutputPath = f;
        });
        BuildCommand = new RelayCommand(_ => Build(),
            _ => !Busy && SourceFolder.Length > 0 && OutputPath.Length > 0);
    }

    private async void Build()
    {
        if (!Directory.Exists(SourceFolder)) { Status = "That source folder doesn't exist."; return; }

        Busy = true;
        Status = "Building tank…";
        string src = SourceFolder, outp = OutputPath, title = TankTitle, author = TankAuthor, desc = Description;
        var when = DateTime.UtcNow;
        try
        {
            var (files, bytes) = await Task.Run(() =>
                TankBuilder.BuildFromFolder(src, outp, title, author, desc, TankPriority.User, when));
            Status = $"Built {Path.GetFileName(outp)} — {files:N0} file(s), {Format.Bytes(bytes)}";
        }
        catch (Exception ex)
        {
            Status = "Build failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}
