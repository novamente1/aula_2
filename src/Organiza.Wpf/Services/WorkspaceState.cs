using Organiza.Wpf.Mvvm;
using Organiza.Domain.Selection;
using Organiza.Domain.Files;

namespace Organiza.Wpf.Services;

public sealed class WorkspaceState : ObservableObject
{
    private string? _rootPath;
    private bool _includeSubfolders;
    private FolderOrganizationMode _organizationMode = FolderOrganizationMode.OrganizeFilesOnly;
    private WorkspaceValidationResult? _lastValidation;
    private bool _folderConfirmed;
    private bool _duplicateScanCompleted;
    private bool _duplicateResolutionCompleted;
    private bool _suggestionsReady;
    private bool _renameApplied;
    private bool _isOperationBusy;
    private string _currentOperation = string.Empty;
    private readonly Dictionary<string, ExactDuplicateReference> _exactDuplicates =
        new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceState()
    {
        // Cada abertura começa sem pasta selecionada. Isso evita executar um novo lote
        // acidentalmente sobre o último dossiê utilizado.
    }

    public string? RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public FolderOrganizationMode OrganizationMode
    {
        get => _organizationMode;
        set => SetProperty(ref _organizationMode, value);
    }

    public WorkspaceValidationResult? LastValidation
    {
        get => _lastValidation;
        set => SetProperty(ref _lastValidation, value);
    }

    public bool HasSelection => !string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath);
    public bool FolderConfirmed { get => _folderConfirmed; private set => SetProperty(ref _folderConfirmed, value); }
    public bool DuplicateScanCompleted { get => _duplicateScanCompleted; private set => SetProperty(ref _duplicateScanCompleted, value); }
    public bool DuplicateResolutionCompleted { get => _duplicateResolutionCompleted; private set => SetProperty(ref _duplicateResolutionCompleted, value); }
    public bool SuggestionsReady { get => _suggestionsReady; private set => SetProperty(ref _suggestionsReady, value); }
    public bool RenameApplied { get => _renameApplied; private set => SetProperty(ref _renameApplied, value); }
    public bool IsOperationBusy { get => _isOperationBusy; private set => SetProperty(ref _isOperationBusy, value); }
    public string CurrentOperation { get => _currentOperation; private set => SetProperty(ref _currentOperation, value); }

    public void ConfirmSelection(string rootPath, bool includeSubfolders, FolderOrganizationMode mode)
    {
        RootPath = Path.GetFullPath(rootPath);
        IncludeSubfolders = includeSubfolders;
        OrganizationMode = mode;
        FolderConfirmed = true;
        DuplicateScanCompleted = false;
        DuplicateResolutionCompleted = false;
        SuggestionsReady = false;
        RenameApplied = false;
        _exactDuplicates.Clear();
    }

    public IReadOnlyDictionary<string, ExactDuplicateReference> ExactDuplicates => _exactDuplicates;

    public void MarkDuplicateScan(IReadOnlyList<DuplicateGroup> groups)
    {
        _exactDuplicates.Clear();
        foreach (var group in groups)
        foreach (var copy in group.Files.Where(item => !item.IsSuggestedPrincipal))
            _exactDuplicates[Path.GetFullPath(copy.File.FullPath)] = new(
                group.Principal.File.FullPath, group.Sha256, group.SizeBytes);
        DuplicateScanCompleted = true;
        DuplicateResolutionCompleted = groups.Count == 0;
        SuggestionsReady = false;
        RenameApplied = false;
    }

    public void MarkDuplicatesResolved() => DuplicateResolutionCompleted = true;

    public void MarkSuggestionsReady()
    {
        SuggestionsReady = true;
        RenameApplied = false;
    }

    public void MarkRenameApplied() => RenameApplied = true;

    public void BeginOperation(string description)
    {
        CurrentOperation = description;
        IsOperationBusy = true;
    }

    public void UpdateOperation(string description)
    {
        if (IsOperationBusy && !string.IsNullOrWhiteSpace(description)) CurrentOperation = description;
    }

    public void EndOperation()
    {
        IsOperationBusy = false;
        CurrentOperation = string.Empty;
    }

}

public sealed record ExactDuplicateReference(string PrincipalPath, string Sha256, long SizeBytes);
