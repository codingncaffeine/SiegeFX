using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using SiegeSmith.Mvvm;

namespace SiegeSmith.ViewModels.ProjectFiles;

/// <summary>Browses and manages a mod project's on-disk source folder: a live file tree with new
/// folder, inline rename, delete-to-Recycle-Bin, reveal, open-externally, plus drag-drop move
/// (within the tree) and import (from Windows). A <see cref="FileSystemWatcher"/> keeps the tree in
/// sync. This is the in-app surface for organizing the loose files that get packaged into a mod.</summary>
public sealed class ProjectFilesViewModel : ObservableObject, IDisposable
{
    private readonly string _root;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce;

    public string RootPath => _root;
    public ObservableCollection<ProjectNodeViewModel> Roots { get; } = new();

    private ProjectNodeViewModel? _selected;
    public ProjectNodeViewModel? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) RaiseCommands(); }
    }

    private string _status = "";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public RelayCommand NewFolderCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand OpenCommand { get; }

    public ProjectFilesViewModel(string root)
    {
        _root = root;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Build(); };

        NewFolderCommand = new RelayCommand(_ => NewFolder());
        RenameCommand = new RelayCommand(_ => Selected?.BeginEdit(), _ => Selected is not null);
        DeleteCommand = new RelayCommand(_ => Delete(), _ => Selected is not null);
        RefreshCommand = new RelayCommand(_ => Build());
        RevealCommand = new RelayCommand(_ => Reveal(), _ => Selected is not null);
        OpenCommand = new RelayCommand(_ => OpenExternally(), _ => Selected is { IsDirectory: false });

        Build();
        SetupWatcher();
    }

    private void RaiseCommands()
    {
        RenameCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        RevealCommand.RaiseCanExecuteChanged();
        OpenCommand.RaiseCanExecuteChanged();
    }

    // ── tree build ──────────────────────────────────────────────
    private void Build()
    {
        Roots.Clear();
        if (!Directory.Exists(_root)) { Status = "Project source folder not found: " + _root; return; }
        try
        {
            foreach (var n in Enumerate(_root)) Roots.Add(n);
            Status = _root;
        }
        catch (Exception ex) { Status = "Read failed: " + ex.Message; }
    }

    private static IEnumerable<ProjectNodeViewModel> Enumerate(string dir)
    {
        foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var node = new ProjectNodeViewModel(d, true);
            foreach (var c in Enumerate(d)) node.Children.Add(c);
            yield return node;
        }
        foreach (var f in Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            yield return new ProjectNodeViewModel(f, false);
    }

    private string TargetDirFor(ProjectNodeViewModel? node)
    {
        if (node is null) return _root;
        return node.IsDirectory ? node.FullPath : (Path.GetDirectoryName(node.FullPath) ?? _root);
    }

    // ── toolbar operations ──────────────────────────────────────
    private void NewFolder()
    {
        var baseDir = TargetDirFor(Selected);
        try
        {
            const string name = "New Folder";
            var path = Path.Combine(baseDir, name);
            int i = 2;
            while (Directory.Exists(path) || File.Exists(path)) path = Path.Combine(baseDir, $"{name} {i++}");
            Directory.CreateDirectory(path);
            Status = "Created " + path;
        }
        catch (Exception ex) { Status = "New folder failed: " + ex.Message; }
    }

    private void Delete()
    {
        if (Selected is null) return;
        var path = Selected.FullPath;
        try
        {
            if (Selected.IsDirectory)
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            Status = "Sent to Recycle Bin: " + path;
        }
        catch (Exception ex) { Status = "Delete failed: " + ex.Message; }
    }

    private void Reveal()
    {
        if (Selected is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Selected.FullPath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Status = "Reveal failed: " + ex.Message; }
    }

    private void OpenExternally()
    {
        if (Selected is not { IsDirectory: false } file) return;
        try { Process.Start(new ProcessStartInfo(file.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) { Status = "Open failed: " + ex.Message; }
    }

    // ── called by the window's rename + drag-drop handlers ──────
    public void CommitRename(ProjectNodeViewModel node)
    {
        var newName = node.EditName?.Trim() ?? "";
        node.IsEditing = false;
        if (string.IsNullOrEmpty(newName) || newName == node.Name) return;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Status = "Invalid file name."; return; }
        try
        {
            var parent = Path.GetDirectoryName(node.FullPath) ?? _root;
            var dest = Path.Combine(parent, newName);
            if (node.IsDirectory) Directory.Move(node.FullPath, dest);
            else File.Move(node.FullPath, dest);
            node.UpdatePath(dest);
            Status = "Renamed to " + newName;
        }
        catch (Exception ex) { Status = "Rename failed: " + ex.Message; }
    }

    public void MoveInto(string sourcePath, ProjectNodeViewModel? targetNode)
    {
        var targetDir = TargetDirFor(targetNode);
        try
        {
            var name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var dest = Path.Combine(targetDir, name);
            if (string.Equals(sourcePath, dest, StringComparison.OrdinalIgnoreCase)) return;
            if (Directory.Exists(sourcePath) &&
                dest.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Status = "Can't move a folder into itself.";
                return;
            }
            if (Directory.Exists(sourcePath)) Directory.Move(sourcePath, dest);
            else if (File.Exists(sourcePath)) File.Move(sourcePath, dest, overwrite: false);
            Status = "Moved " + name;
        }
        catch (Exception ex) { Status = "Move failed: " + ex.Message; }
    }

    public void Import(IEnumerable<string> paths, ProjectNodeViewModel? targetNode)
    {
        var targetDir = TargetDirFor(targetNode);
        int n = 0;
        foreach (var src in paths)
        {
            try
            {
                var name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var dest = Path.Combine(targetDir, name);
                if (Directory.Exists(src)) CopyDir(src, dest);
                else if (File.Exists(src)) File.Copy(src, dest, overwrite: true);
                n++;
            }
            catch (Exception ex) { Status = "Import failed: " + ex.Message; }
        }
        if (n > 0) Status = $"Imported {n} item(s) into {Path.GetFileName(targetDir)}";
    }

    /// <summary>True when <paramref name="path"/> lives inside this project's source tree — used to
    /// decide move (internal) vs import (external) on a drop.</summary>
    public bool IsInsideRoot(string path) =>
        path.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.EnumerateFiles(src))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        foreach (var d in Directory.EnumerateDirectories(src))
            CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    // ── watcher ─────────────────────────────────────────────────
    private void SetupWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFsChanged;
            _watcher.Deleted += OnFsChanged;
            _watcher.Renamed += OnFsChanged;
        }
        catch { /* watcher is a nicety; ignore if it can't attach */ }
    }

    private void OnFsChanged(object sender, FileSystemEventArgs e)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
