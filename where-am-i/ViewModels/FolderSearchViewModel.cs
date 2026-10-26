using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using where_am_i.Commands;
using where_am_i.Services;

namespace where_am_i.ViewModels;

public sealed class FolderSearchViewModel : INotifyPropertyChanged
{
    private bool isSearching;
    public bool IsSearching { get => isSearching; set { isSearching = value; Changed(); SearchCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); } }
    private string status = "기본 경로에서 찾지 못하면 전체 탐색을 실행해 주세요.";
    public string Status { get => status; set { status = value; Changed(); } }
    public ObservableCollection<DiscoveredFolder> Results { get; } = new();
    private DiscoveredFolder? selected;
    public DiscoveredFolder? Selected { get => selected; set { selected = value; Changed(); ApplyCommand.RaiseCanExecuteChanged(); } }
    public RelayCommand SearchCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public FolderSearchViewModel(Action search, Action cancel, Action apply)
    {
        SearchCommand = new RelayCommand(search, () => !IsSearching);
        CancelCommand = new RelayCommand(cancel, () => IsSearching);
        ApplyCommand = new RelayCommand(apply, () => Selected is not null && !IsSearching);
    }
    public void SetResults(IEnumerable<DiscoveredFolder> folders)
    {
        Results.Clear();
        foreach (var folder in folders) Results.Add(folder);
        Selected = Results.FirstOrDefault();
        Status = Results.Count == 0 ? "탐색 완료 · 후보 없음 · 폴더를 직접 선택해 주세요." : $"탐색 완료 · {Results.Count}개 후보 · 사용할 폴더를 선택해 적용해 주세요.";
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
