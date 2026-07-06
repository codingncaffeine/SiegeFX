using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiegeSmith.Mvvm;

/// <summary>Minimal <see cref="INotifyPropertyChanged"/> base for view-models.
/// SiegeSmith deliberately avoids an MVVM framework dependency — the shell is
/// small enough that a hand-rolled base keeps the build lean and the code obvious.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Assigns <paramref name="value"/> to <paramref name="field"/> and raises
    /// <see cref="PropertyChanged"/> when it actually changes. Returns whether it changed.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
