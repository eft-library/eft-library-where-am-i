using System.ComponentModel;
using System.Runtime.CompilerServices;
using where_am_i.Services;

namespace where_am_i.ViewModels;

public sealed class FolderSearchViewModel(string label, string? savedPath) : INotifyPropertyChanged
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private string? registeredPath = string.IsNullOrWhiteSpace(savedPath) ? null : savedPath;
    private string? selectedPath = string.IsNullOrWhiteSpace(savedPath) ? null : savedPath;
    private string? manualPath;
    private string[] discoveredPaths = [];
    private string[] automaticPaths = [];
    private bool searched;
    private string? error;

    public IReadOnlyList<string> Paths { get; private set; } =
        string.IsNullOrWhiteSpace(savedPath) ? [] : [savedPath];
    public bool HasPaths => Paths.Count > 0;
    public bool HasPendingPath => SelectedPath is not null && !PathComparer.Equals(SelectedPath, registeredPath);
    public bool IsDiscoveredSelection => SelectedPath is not null &&
        !PathComparer.Equals(SelectedPath, manualPath) && discoveredPaths.Contains(SelectedPath, PathComparer);
    public string Status => error ?? (registeredPath is not null
        ? $"{label} 등록됨 · {registeredPath}"
        : searched && SelectedPath is null ? $"{label}를 찾지 못했습니다. 직접 선택해 주세요."
        : $"{label} 미등록");

    public string? SelectedPath
    {
        get => selectedPath;
        set
        {
            if (value is null || selectedPath == value) return;
            selectedPath = value;
            error = null;
            Changed();
            Changed(nameof(Status));
            Changed(nameof(HasPendingPath));
        }
    }

    public void SetAutomaticPaths(IEnumerable<string> paths)
    {
        var candidates = paths.Distinct(PathComparer).ToArray();
        if (automaticPaths.SequenceEqual(candidates, PathComparer)) return;

        automaticPaths = candidates;
        SetPaths(GetAvailableSelection());
    }

    public void SetResults(IEnumerable<DiscoveredFolder> folders)
    {
        searched = true;
        discoveredPaths = folders.Select(folder => folder.Path).ToArray();
        error = null;
        SetPaths(GetAvailableSelection());
    }

    public void SelectManualPath(string path)
    {
        error = null;
        manualPath = path;
        SetPaths(path);
    }

    public void SetRegisteredPath(string path)
    {
        error = null;
        registeredPath = path;
        manualPath = null;
        SetPaths(path);
    }

    public void SetError()
    {
        error = $"{label}를 등록하지 못했습니다. 경로를 확인해 주세요.";
        Changed(nameof(Status));
    }

    private string? GetAvailableSelection()
    {
        if (SelectedPath is { } selected &&
            (PathComparer.Equals(selected, registeredPath) || PathComparer.Equals(selected, manualPath) ||
             discoveredPaths.Contains(selected, PathComparer) || automaticPaths.Contains(selected, PathComparer)))
        {
            return selected;
        }

        return registeredPath ?? manualPath;
    }

    private void SetPaths(string? preferredPath)
    {
        Paths = new[] { preferredPath, registeredPath, manualPath }.OfType<string>()
            .Concat(discoveredPaths).Concat(automaticPaths).Distinct(PathComparer).ToArray();
        // Update the source before notifying the selector that its items changed.
        selectedPath = Paths.FirstOrDefault();
        Changed(nameof(Paths));
        Changed(nameof(HasPaths));
        Changed(nameof(HasPendingPath));
        Changed(nameof(SelectedPath));
        Changed(nameof(Status));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
