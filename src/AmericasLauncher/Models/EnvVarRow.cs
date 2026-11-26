// SPDX-License-Identifier: GPL-2.0-only
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AmericasLauncher.Models;

/// <summary>One editable row (name + value) in Settings > Launch Options.</summary>
public sealed class EnvVarRow : INotifyPropertyChanged
{
    private string _name = "";
    private string _value = "";
    private string? _error;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string Value { get => _value; set => Set(ref _value, value ?? ""); }

    /// <summary>Why this row is not saved, or null when it is fine.</summary>
    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => _error != null;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
