using System.Windows.Input;
using Organiza.Wpf.Mvvm;
using Organiza.Wpf.Services;

namespace Organiza.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private object _currentPage;
    private readonly WorkspaceState _state;
    private readonly RelayCommand _selectFolderCommand;
    private readonly RelayCommand _duplicatesCommand;
    private readonly RelayCommand _pdfSplitCommand;
    private readonly RelayCommand _organizeCommand;
    private readonly RelayCommand _historyCommand;
    private readonly RelayCommand _settingsCommand;

    public MainViewModel(
        WorkspaceState state,
        FolderSelectionViewModel folderSelection,
        DuplicatesViewModel duplicates,
        PdfSplitViewModel pdfSplit,
        OrganizeViewModel organize,
        HistoryViewModel history,
        SettingsViewModel settings)
    {
        _state = state;
        _currentPage = folderSelection;
        _selectFolderCommand = new RelayCommand(() => CurrentPage = folderSelection, () => !IsBusy);
        _duplicatesCommand = new RelayCommand(() => CurrentPage = duplicates, () => !IsBusy && state.FolderConfirmed);
        _organizeCommand = new RelayCommand(() => CurrentPage = organize, () => !IsBusy && state.DuplicateResolutionCompleted);
        _pdfSplitCommand = new RelayCommand(() => CurrentPage = pdfSplit, () => !IsBusy && state.RenameApplied);
        _historyCommand = new RelayCommand(() => CurrentPage = history, () => !IsBusy && state.FolderConfirmed);
        _settingsCommand = new RelayCommand(() => CurrentPage = settings, () => !IsBusy);
        state.PropertyChanged += (_, _) => RefreshWorkflow();
    }
    public string AppTitle => OrganizaApplicationInfo.DisplayName;
    public bool IsBusy => _state.IsOperationBusy;
    public string CurrentOperation => _state.CurrentOperation;
    public string WorkflowStatus => !_state.FolderConfirmed ? "Confirme a Etapa 1 para começar."
        : !_state.DuplicateResolutionCompleted ? "Etapa 2 pendente: verifique e trate as duplicidades."
        : _state.RenameApplied ? "Etapa 3 concluída: alterações físicas registradas."
        : !_state.SuggestionsReady ? "Etapa 3: gere o Livro Mestre e as sugestões."
        : !_state.RenameApplied ? "Etapa 3: sugestões prontas; falta aplicar as alterações físicas."
        : "Etapa 3 concluída. Divisão de PDFs e relatório foram liberados.";

    public object CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value) && value is IActivatablePage activatable)
            {
                activatable.Activate();
            }
        }
    }
    public ICommand SelectFolderCommand => _selectFolderCommand;
    public ICommand DuplicatesCommand => _duplicatesCommand;
    public ICommand PdfSplitCommand => _pdfSplitCommand;
    public ICommand OrganizeCommand => _organizeCommand;
    public ICommand HistoryCommand => _historyCommand;
    public ICommand SettingsCommand => _settingsCommand;

    private void RefreshWorkflow()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CurrentOperation));
        OnPropertyChanged(nameof(WorkflowStatus));
        _selectFolderCommand.NotifyCanExecuteChanged();
        _duplicatesCommand.NotifyCanExecuteChanged();
        _organizeCommand.NotifyCanExecuteChanged();
        _pdfSplitCommand.NotifyCanExecuteChanged();
        _historyCommand.NotifyCanExecuteChanged();
        _settingsCommand.NotifyCanExecuteChanged();
    }
}
