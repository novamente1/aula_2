using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Organiza.Application.Abstractions;
using Organiza.Application.Services;
using Organiza.Domain.Files;
using Organiza.Domain.Operations;
using Organiza.Infrastructure.History;
using Organiza.Wpf.Mvvm;
using Organiza.Wpf.Services;

namespace Organiza.Wpf.ViewModels;

public sealed class DuplicateCandidateViewModel(DuplicateCandidate candidate, string rootPath) : ObservableObject
{
    private bool _isSelected = !candidate.IsSuggestedPrincipal;
    public string FullPath => candidate.File.FullPath;
    public string RelativePath => Path.GetRelativePath(rootPath, candidate.File.FullPath);
    public bool IsSuggestedPrincipal => candidate.IsSuggestedPrincipal;
    public bool CanMove => !IsSuggestedPrincipal;
    public string ActionLabel => IsSuggestedPrincipal ? "MANTER" : "MOVER CÓPIA";
    public bool IsSelected
    {
        get => _isSelected;
        set { if (CanMove) SetProperty(ref _isSelected, value); }
    }
}

public sealed class DuplicateGroupViewModel(DuplicateGroup group, string rootPath)
{
    public long SizeBytes => group.SizeBytes;
    public string Sha256 => group.Sha256;
    public ObservableCollection<DuplicateCandidateViewModel> Files { get; } =
        new(group.Files.Select(file => new DuplicateCandidateViewModel(file, rootPath)));
}

public sealed class DuplicatesViewModel : ObservableObject
{
    private readonly WorkspaceState _state;
    private readonly IFileSystem _fileSystem;
    private readonly IHashCalculator _hash;
    private readonly StandardFolderManager _folders;
    private readonly PathGuard _pathGuard;
    private readonly AsyncRelayCommand _scanCommand;
    private readonly AsyncRelayCommand _moveCommand;
    private readonly RelayCommand _confirmReviewCommand;
    private string _status = "A verificação usa tamanho + SHA-256 completo. Nomes não são comparados.";
    private string _operationStatus = string.Empty;
    private bool _isBusy;

    public DuplicatesViewModel(WorkspaceState state, IFileSystem fileSystem, IHashCalculator hash,
        StandardFolderManager folders, PathGuard pathGuard)
    {
        _state = state;
        _fileSystem = fileSystem;
        _hash = hash;
        _folders = folders;
        _pathGuard = pathGuard;
        _scanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        _moveCommand = new AsyncRelayCommand(MoveAsync, () => !IsBusy &&
            Groups.SelectMany(group => group.Files).Any(file => file.IsSelected && file.CanMove));
        _confirmReviewCommand = new RelayCommand(ConfirmReview,
            () => !IsBusy && _state.DuplicateScanCompleted && !_state.DuplicateResolutionCompleted);
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string OperationStatus
    {
        get => _operationStatus;
        private set { if (SetProperty(ref _operationStatus, value)) _state.UpdateOperation(value); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            if (value) _state.BeginOperation(string.IsNullOrWhiteSpace(OperationStatus) ? "Verificando duplicidades..." : OperationStatus);
            else _state.EndOperation();
            _scanCommand.NotifyCanExecuteChanged();
            _moveCommand.NotifyCanExecuteChanged();
            _confirmReviewCommand.NotifyCanExecuteChanged();
        }
    }
    public ICommand ScanCommand => _scanCommand;
    public ICommand MoveCommand => _moveCommand;
    public ICommand ConfirmReviewCommand => _confirmReviewCommand;

    private async Task ScanAsync()
    {
        if (!_state.HasSelection)
        {
            MessageBox.Show("Selecione uma pasta na etapa 1.", "Organiza", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        var visibleProgress = Stopwatch.StartNew();
        OperationStatus = "Calculando tamanho e SHA-256 integral de cada arquivo...";
        try
        {
            await RefreshGroupsAsync();
            OperationStatus = Groups.Count == 0
                ? "Nenhuma cópia exata: Etapa 2 concluída automaticamente."
                : "Cópias exatas encontradas e pré-selecionadas. Escolha mover ou manter no lugar.";
        }
        catch (Exception exception)
        {
            Status = "Verificação interrompida.";
            MessageBox.Show(exception.Message, "Erro na verificação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            var remaining = TimeSpan.FromMilliseconds(900) - visibleProgress.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            IsBusy = false;
        }
    }

    private async Task MoveAsync()
    {
        var selected = Groups.SelectMany(group => group.Files)
            .Where(file => file.IsSelected && file.CanMove)
            .Select(file => file.FullPath)
            .ToArray();
        if (selected.Length == 0) return;
        var destinationDecision = new DuplicateDestinationResolver(_fileSystem).Resolve(_state.RootPath!);
        var duplicatesDirectory = Path.Combine(_state.RootPath!, destinationDecision.FolderName);
        var possibleDestinations = selected.Select(path => Path.Combine(duplicatesDirectory,
            PathNameShortener.FitFileName(duplicatesDirectory, Path.GetFileName(path))));
        if (!ConfirmWarnings(possibleDestinations)) return;
        if (MessageBox.Show($"Mover {selected.Length} cópia(s) para {destinationDecision.FolderName}?\n\n" +
                $"Regra aplicada: {destinationDecision.RuleCode} — {destinationDecision.Explanation}\n\n" +
                "Nenhum arquivo será excluído; pastas vazias não estruturais poderão ser removidas.",
                "Confirmação obrigatória", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        IsBusy = true;
        OperationStatus = $"Movendo {selected.Length} cópia(s) confirmada(s) e atualizando a varredura...";
        try
        {
            var cleaner = new EmptyFolderCleaner(_fileSystem);
            var mover = new DuplicateMover(_fileSystem, _folders, _pathGuard,
                new JsonHistoryStore(_state.RootPath!), cleaner);
            await mover.MoveConfirmedAsync(_state.RootPath!, selected,
                ExplicitApproval.Grant("Usuário confirmou as cópias marcadas"));
            await RefreshGroupsAsync();
            OperationStatus = "Movimentação concluída. Prossiga para a Etapa 3.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Movimentação bloqueada", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ConfirmReview()
    {
        var copyCount = Groups.SelectMany(group => group.Files).Count(file => file.CanMove);
        if (copyCount == 0) return;
        if (MessageBox.Show(
                $"Concluir a Etapa 2 mantendo {copyCount} cópia(s) identificada(s) nos locais atuais? " +
                "Nenhum arquivo será movido ou excluído. Você poderá voltar e executar uma nova verificação depois.",
                "Concluir revisão sem mover", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _state.MarkDuplicatesResolved();
        Status = "Revisão de duplicidades concluída por decisão do usuário. As cópias permaneceram intactas e a Etapa 3 foi liberada.";
        OperationStatus = "Etapa 2 concluída sem movimentar arquivos.";
        _confirmReviewCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshGroupsAsync()
    {
        Status = "Calculando hashes completos...";
        Groups.Clear();
        var option = _state.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var progress = new Progress<DuplicateScanProgress>(update =>
            OperationStatus = update.Total == 0
                ? update.Message
                : $"{update.Message} ({update.Current}/{update.Total})");
        var scan = await Task.Run(async () =>
        {
            var scannedFiles = _fileSystem.EnumerateFiles(_state.RootPath!, option)
                .Count(path => WorkspaceFilePolicy.IsInteractiveCandidate(_state.RootPath!, path));
            var groups = await new DuplicateFinder(_fileSystem, _hash)
                .FindAsync(_state.RootPath!, _state.IncludeSubfolders, progress);
            return (ScannedFiles: scannedFiles, Groups: groups);
        });
        var scannedFiles = scan.ScannedFiles;
        var groups = scan.Groups;
        foreach (var group in groups)
        {
            var viewModel = new DuplicateGroupViewModel(group, _state.RootPath!);
            foreach (var file in viewModel.Files)
                file.PropertyChanged += (_, _) => _moveCommand.NotifyCanExecuteChanged();
            Groups.Add(viewModel);
        }
        _state.MarkDuplicateScan(groups);
        _moveCommand.NotifyCanExecuteChanged();
        var completedAt = DateTime.Now.ToString("HH:mm:ss");
        Status = groups.Count == 0
            ? $"CONCLUÍDO às {completedAt}: {scannedFiles} documento(s) examinados e nenhuma cópia exata. A Etapa 3 já está liberada; não há nada para mover."
            : $"{groups.Count} grupo(s) de cópias exatas encontrado(s) em {scannedFiles} documento(s). As cópias já estão selecionadas; o principal marcado MANTER fica protegido. Escolha mover para a pasta indicada ou manter no local e continuar.";
    }

    private bool ConfirmWarnings(IEnumerable<string> paths)
    {
        var assessments = paths.Select(_pathGuard.Assess).ToArray();
        var blocked = assessments.Where(result => result.Risk == PathRisk.Blocked).ToArray();
        if (blocked.Length > 0)
        {
            MessageBox.Show(
                $"A movimentação foi bloqueada porque {blocked.Length} caminho(s) previsto(s) ultrapassam o limite máximo de 240 caracteres. " +
                "Nenhum arquivo foi alterado. Reduza manualmente os nomes ou mova o dossiê para um caminho mais curto.",
                "Caminho longo — movimentação bloqueada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var warnings = assessments.Where(result => result.Risk == PathRisk.Warning).ToArray();
        if (warnings.Length == 0) return true;
        return MessageBox.Show(
            $"Atenção: {warnings.Length} caminho(s) terão mais de 220 caracteres, mas continuarão dentro do limite de 240. Deseja continuar?",
            "Caminho longo", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
