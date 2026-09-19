using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Organiza.Application.Abstractions;
using Organiza.Application.Services;
using Organiza.Domain.Files;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;
using Organiza.Domain.MasterBook;
using Organiza.Infrastructure.History;
using Organiza.Wpf.Mvvm;
using Organiza.Wpf.Services;

namespace Organiza.Wpf.ViewModels;

public sealed class RenameItemViewModel : ObservableObject
{
    private bool _isSelected;
    private string _suggestedName;
    private string _reviewStatus = "Pendente";
    private bool _isReviewApproved;

    public RenameItemViewModel(string originalPath, string suggestedName, string? reason = null,
        string? destinationFolder = null, string? classificationRule = null, string? documentType = null,
        string? namingProtocolStatus = null, string? qualityStatus = null,
        ExactDuplicateReference? exactDuplicate = null,
        string? principalId = null, string? secondaryId = null)
    {
        OriginalPath = originalPath;
        _suggestedName = suggestedName;
        Reason = reason;
        DestinationFolder = destinationFolder;
        ClassificationRule = classificationRule ?? "—";
        DocumentType = documentType ?? "—";
        NamingProtocolStatus = namingProtocolStatus ?? "Aguardando análise";
        QualityStatus = qualityStatus ?? "Aguardando análise";
        ExactDuplicate = exactDuplicate;
        PrincipalId = principalId;
        SecondaryId = secondaryId;
    }

    public string OriginalPath { get; }
    public string OriginalName => Path.GetFileName(OriginalPath);
    public string SuggestedName
    {
        get => _suggestedName;
        set
        {
            if (!SetProperty(ref _suggestedName, value)) return;
            IsReviewApproved = false;
            ReviewStatus = "Pendente após edição";
        }
    }
    public string? Reason { get; }
    public string? DestinationFolder { get; }
    public string ClassificationRule { get; }
    public string DocumentType { get; }
    public string NamingProtocolStatus { get; }
    public string QualityStatus { get; }
    public ExactDuplicateReference? ExactDuplicate { get; }
    public bool IsExactDuplicate => ExactDuplicate is not null;
    public string DuplicateOf => ExactDuplicate is null ? "—" : Path.GetFileName(ExactDuplicate.PrincipalPath);
    public string? PrincipalId { get; }
    public string? SecondaryId { get; }
    public string ApprovalBrush => IsReviewApproved ? "#227447" : "#7A8796";
    public bool IsSelected
    {
        get => _isSelected;
        set { if (!IsExactDuplicate) SetProperty(ref _isSelected, value); }
    }
    public string ReviewStatus { get => _reviewStatus; set => SetProperty(ref _reviewStatus, value); }
    public bool IsReviewApproved
    {
        get => _isReviewApproved;
        set
        {
            if (SetProperty(ref _isReviewApproved, value)) OnPropertyChanged(nameof(ApprovalBrush));
        }
    }
}

public sealed class OrganizeViewModel : ObservableObject, IActivatablePage
{
    private readonly WorkspaceState _state;
    private readonly IFileSystem _fileSystem;
    private readonly IContentSuggestionGateway _suggestionGateway;
    private readonly IHashCalculator _hash;
    private readonly DocumentAnalysisService _documentAnalysis;
    private readonly AsyncRelayCommand _generateCommand;
    private readonly AsyncRelayCommand _applyCommand;
    private readonly AsyncRelayCommand _masterBookCommand;
    private readonly ParameterizedRelayCommand _openFileCommand;
    private readonly ParameterizedRelayCommand _editNameCommand;
    private readonly ParameterizedRelayCommand _markCorrectCommand;
    private readonly ParameterizedRelayCommand _reanalyzeAllCommand;
    private string _status = "Nenhum conteúdo é lido automaticamente.";
    private string _operationStatus = string.Empty;
    private bool _isBusy;
    private bool _suggestionsReady;
    private string _lockedFilesAlert = string.Empty;

    public OrganizeViewModel(WorkspaceState state, IFileSystem fileSystem, IContentSuggestionGateway suggestionGateway,
        DocumentAnalysisService documentAnalysis, IHashCalculator hash)
    {
        _state = state;
        _fileSystem = fileSystem;
        _suggestionGateway = suggestionGateway;
        _documentAnalysis = documentAnalysis;
        _hash = hash;
        _generateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy);
        // O botão permanece acionável depois da geração para poder explicar por que
        // a aplicação ainda está bloqueada. Desabilitá-lo silenciosamente fazia o
        // operador concluir, corretamente, que a função não estava respondendo.
        _applyCommand = new AsyncRelayCommand(ApplyAsync,
            () => !IsBusy && SuggestionsReady && Items.Count > 0);
        _masterBookCommand = new AsyncRelayCommand(GenerateMasterBookAsync, () => !IsBusy);
        _openFileCommand = new ParameterizedRelayCommand(OpenFile,
            parameter => !IsBusy && parameter is string path && _fileSystem.FileExists(path));
        _editNameCommand = new ParameterizedRelayCommand(EditName,
            parameter => !IsBusy && SuggestionsReady && parameter is RenameItemViewModel item && !item.IsExactDuplicate);
        _markCorrectCommand = new ParameterizedRelayCommand(
            MarkCorrect, parameter => !IsBusy && parameter is RenameItemViewModel item && !item.IsExactDuplicate);
        _reanalyzeAllCommand = new ParameterizedRelayCommand(
            parameter => _ = ReanalyzeAllFromReviewAsync(parameter),
            parameter => !IsBusy && SuggestionsReady && parameter is RenameItemViewModel item && !item.IsExactDuplicate);
    }

    public RangeObservableCollection<RenameItemViewModel> Items { get; } = [];
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
            if (value) _state.BeginOperation(string.IsNullOrWhiteSpace(OperationStatus) ? "Preparando a análise documental..." : OperationStatus);
            else _state.EndOperation();
            _generateCommand.NotifyCanExecuteChanged();
            _applyCommand.NotifyCanExecuteChanged();
            _masterBookCommand.NotifyCanExecuteChanged();
            _openFileCommand.NotifyCanExecuteChanged();
            _editNameCommand.NotifyCanExecuteChanged();
            _markCorrectCommand.NotifyCanExecuteChanged();
            _reanalyzeAllCommand.NotifyCanExecuteChanged();
        }
    }
    public bool SuggestionsReady
    {
        get => _suggestionsReady;
        private set
        {
            if (!SetProperty(ref _suggestionsReady, value)) return;
            _applyCommand.NotifyCanExecuteChanged();
            _reanalyzeAllCommand.NotifyCanExecuteChanged();
        }
    }
    public string LockedFilesAlert
    {
        get => _lockedFilesAlert;
        private set => SetProperty(ref _lockedFilesAlert, value);
    }
    public ICommand GenerateCommand => _generateCommand;
    public ICommand ApplyCommand => _applyCommand;
    public ICommand MasterBookCommand => _masterBookCommand;
    public ICommand OpenFileCommand => _openFileCommand;
    public ICommand EditNameCommand => _editNameCommand;
    public ICommand MarkCorrectCommand => _markCorrectCommand;
    public ICommand ReanalyzeAllCommand => _reanalyzeAllCommand;
    public string FolderModeNotice => _state.OrganizationMode == Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure
        ? "Estrutura padrão ativa: os arquivos aprovados serão movidos para as pastas oficiais indicadas."
        : "Modo somente arquivos ativo: as pastas oficiais são apenas uma referência e os arquivos permanecerão em suas pastas atuais. Para classificá-los fisicamente, volte à Etapa 1 e escolha Aplicar a estrutura padrão.";

    public async void Activate()
    {
        await LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        var ownsBusyState = !IsBusy;
        if (ownsBusyState) IsBusy = true;
        OnPropertyChanged(nameof(FolderModeNotice));
        LockedFilesAlert = string.Empty;
        SuggestionsReady = false;
        Items.Clear();
        if (!_state.HasSelection)
        {
            Status = "Selecione uma pasta na etapa 1.";
            if (ownsBusyState) IsBusy = false;
            return;
        }

        OperationStatus = "Carregando o inventário em segundo plano...";
        try
        {
            var loaded = await Task.Run(() =>
            {
                var option = _state.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return _fileSystem.EnumerateFiles(_state.RootPath!, option)
                    .Where(path => WorkspaceFilePolicy.IsInteractiveCandidate(_state.RootPath!, path))
                    .OrderBy(path => _state.ExactDuplicates.ContainsKey(Path.GetFullPath(path)) ? 1 : 0)
                    .ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(path =>
                    {
                        _state.ExactDuplicates.TryGetValue(Path.GetFullPath(path), out var duplicate);
                        return duplicate is null
                            ? new RenameItemViewModel(path, "Clique em GERAR NOMES")
                                { IsSelected = true, ReviewStatus = "—" }
                            : new RenameItemViewModel(path, Path.GetFileName(path),
                                "Cópia digital exata comprovada por tamanho + SHA-256. Trate-a na Etapa 2; ela não precisa de novo nome.",
                                classificationRule: "DUPLICADO-EXATO-SHA256", documentType: "Cópia digital exata",
                                namingProtocolStatus: "Não aplicável à cópia exata", qualityStatus: "Integridade confirmada por hash",
                                exactDuplicate: duplicate)
                            { IsSelected = false, IsReviewApproved = true, ReviewStatus = "Cópia exata — Etapa 2" };
                    })
                    .ToArray();
            });
            Items.ReplaceRange(loaded);
            var copies = loaded.Count(item => item.IsExactDuplicate);
            Status = $"{loaded.Length - copies} documento(s) aguardando GERAR NOMES e {copies} cópia(s) digital(is) exata(s) ao final. Antes da análise, nenhum nome exibido é uma sugestão.";
            OperationStatus = $"Inventário carregado: {loaded.Length} arquivo(s). A tabela usa exibição virtualizada.";
        }
        catch (Exception exception)
        {
            Status = "Não foi possível carregar o inventário da pasta.";
            MessageBox.Show(exception.Message, "Inventário interrompido", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ownsBusyState) IsBusy = false;
        }
    }

    private void OpenFile(object? parameter)
    {
        if (parameter is not string path || !_fileSystem.FileExists(path))
        {
            MessageBox.Show("O arquivo original não foi encontrado. Atualize a listagem antes de tentar novamente.",
                "Abrir documento", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = DocumentPreviewLauncher.Open(path);
        if (result.Opened)
        {
            Status = $"Consulta rápida aberta no {result.Viewer}: {Path.GetFileName(path)}. " +
                     "O documento original não foi alterado.";
            return;
        }
        MessageBox.Show($"O Windows não conseguiu abrir este documento.\n\n{result.Error}",
            "Abrir documento", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void EditName(object? parameter)
    {
        if (parameter is not RenameItemViewModel item || !SuggestionsReady) return;
        var edited = Microsoft.VisualBasic.Interaction.InputBox(
            "Edite o nome sugerido. A extensão original será preservada na aplicação final.",
            "Editar nome sugerido", item.SuggestedName);
        if (string.IsNullOrWhiteSpace(edited) ||
            string.Equals(edited, item.SuggestedName, StringComparison.Ordinal)) return;
        item.SuggestedName = edited.Trim();
        item.IsSelected = true;
        Status = $"Nome ajustado manualmente: {item.OriginalName}. Confira o documento e marque ✓ para aprovar.";
    }

    private void MarkCorrect(object? parameter)
    {
        if (parameter is not RenameItemViewModel item || !_state.HasSelection) return;
        item.IsSelected = true;
        item.IsReviewApproved = true;
        item.ReviewStatus = "✓ Certo";
        var pending = Items.Count(candidate => !candidate.IsReviewApproved);
        Status = pending == 0
            ? "CONFERÊNCIA 100% CONCLUÍDA — todas as sugestões foram aprovadas. A aplicação física foi liberada."
            : $"Revisado como CERTO: {item.OriginalName}. Ainda faltam {pending} documento(s) para conferir.";
        _applyCommand.NotifyCanExecuteChanged();
        _ = SaveReviewFeedbackAsync(item, isCorrect: true);
    }

    private async Task SaveReviewFeedbackAsync(RenameItemViewModel item, bool isCorrect)
    {
        try
        {
            var history = new JsonHistoryStore(_state.RootPath!);
            await history.AppendAsync(
            [
                new OperationLogEntry(DateTimeOffset.Now,
                    isCorrect ? "Revisar sugestão — certo" : "Revisar sugestão — errado",
                    item.OriginalPath,
                    item.SuggestedName,
                    Organiza.Domain.Operations.OperationStatus.Completed,
                    isCorrect
                        ? "O operador abriu/conferiu e aprovou a sugestão para compor a remessa. Nenhum arquivo foi alterado nesta revisão."
                        : "O operador reprovou e desmarcou a sugestão para correção manual. Nenhum arquivo foi alterado nesta revisão.")
            ]);
        }
        catch (Exception exception)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show($"A decisão foi aplicada na tela, mas não pôde ser registrada no histórico.\n\n{exception.Message}",
                    "Registro da revisão", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    private async Task ReanalyzeAllFromReviewAsync(object? parameter)
    {
        if (parameter is not RenameItemViewModel rejected || !_state.HasSelection || IsBusy) return;
        var rejectedPath = rejected.OriginalPath;
        var rejectedSuggestion = rejected.SuggestedName;
        rejected.IsSelected = false;
        rejected.IsReviewApproved = false;
        rejected.ReviewStatus = "✗ Refazendo tudo";
        _applyCommand.NotifyCanExecuteChanged();
        IsBusy = true;
        SuggestionsReady = false;
        Status = "Uma sugestão foi reprovada. Todos os documentos serão relidos e toda a lista será recriada.";
        OperationStatus = $"Reiniciando a leitura integral de {Items.Count} documento(s)...";
        try
        {
            await SaveReviewFeedbackAsync(rejected, isCorrect: false);
            var files = await Task.Run(GetListedFiles);
            var result = await BuildSuggestionsAsync(files, forceFreshAnalysis: true);
            await SaveCatalogAsync(result);
            DisplaySuggestions(result.Suggestions);
            var refreshedRejected = Items.FirstOrDefault(item =>
                string.Equals(item.OriginalPath, rejectedPath, StringComparison.OrdinalIgnoreCase));
            var changed = refreshedRejected is not null &&
                          !string.Equals(refreshedRejected.SuggestedName, rejectedSuggestion,
                              StringComparison.OrdinalIgnoreCase);
            if (refreshedRejected is not null && !changed)
            {
                refreshedRejected.IsSelected = false;
                refreshedRejected.ReviewStatus = "⚠ Repetiu — confira";
            }

            Status = changed
                ? "LISTA INTEIRA REFEITA — todas as aprovações anteriores foram zeradas. Abra e confira novamente cada documento."
                : "LISTA INTEIRA REFEITA, mas o item apontado recebeu a mesma sugestão. Ele ficou desmarcado; confira o conteúdo e repita a revisão. Todas as aprovações anteriores foram zeradas.";
            OperationStatus = $"Reanálise integral concluída: {files.Length} documento(s) relido(s); conferência reiniciada.";
        }
        catch (Exception exception)
        {
            SuggestionsReady = true;
            rejected.ReviewStatus = "⚠ Falhou — tente refazer";
            Status = "A reanálise integral foi interrompida sem alterar nenhum documento.";
            MessageBox.Show(exception.Message, "Não foi possível refazer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GenerateMasterBookAsync()
    {
        if (!_state.HasSelection) return;
        if (!await ValidateContextBeforeAnalysisAsync(recursive: true)) return;
        var markdownPath = Path.Combine(_state.RootPath!, MasterBookFileNames.Markdown);
        var jsonPath = Path.Combine(_state.RootPath!, MasterBookFileNames.Json);
        var isUpdate = _fileSystem.FileExists(markdownPath) || _fileSystem.FileExists(jsonPath);
        var message = isUpdate
            ? "Atualizar o Livro Mestre 360° existente? A versão atual será substituída após nova varredura de todos os arquivos e subpastas. A pasta raiz não será renomeada."
            : "Gerar o Livro Mestre 360°? Todos os arquivos e subpastas serão lidos para criar a base .md + .json na raiz. Nenhuma pasta ou arquivo de origem será renomeado.";
        if (MessageBox.Show(message, "Livro Mestre 360°", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Status = "Varredura aprofundada iniciada; arquivos originais permanecem protegidos.";
        OperationStatus = "Preparando inventário documental...";
        try
        {
            var progress = new Progress<MasterBookProgress>(update =>
            {
                OperationStatus = update.Total == 0
                    ? update.Message
                    : $"{update.Message} ({update.Current}/{update.Total})";
            });
            var service = new MasterBookService(_fileSystem, _documentAnalysis, new PathGuard(),
                new JsonHistoryStore(_state.RootPath!));
            var result = await Task.Run(() => service.GenerateAsync(
                _state.RootPath!, ExplicitApproval.Grant("Usuário confirmou o Livro Mestre 360°"), progress));
            await LoadItemsAsync();
            Status = $"Livro Mestre concluído: {result.DocumentsAnalyzed} documento(s) e {result.ProcessesIdentified} processo(s). Nenhuma sugestão foi gerada e nenhum arquivo foi renomeado. Clique em “2. Analisar conteúdo e gerar catálogo” quando desejar preparar os nomes.";
            OperationStatus = "Livro Mestre salvo. A geração de sugestões permanece aguardando autorização separada.";
        }
        catch (Exception exception)
        {
            Status = "A geração do Livro Mestre foi interrompida.";
            MessageBox.Show(exception.Message, "Livro Mestre 360°", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GenerateAsync()
    {
        if (!_state.HasSelection) return;
        if (!await ValidateContextBeforeAnalysisAsync(_state.IncludeSubfolders)) return;
        var answer = MessageBox.Show(
            "Analisar o conteúdo e gerar o catálogo local agora? O processamento ocorre neste computador, " +
            "sem integração externa nesta versão. Nenhum arquivo será renomeado ou movimentado.",
            "Analisar conteúdo e gerar catálogo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var files = await Task.Run(GetListedFiles);
            Status = "Validando a leitura integral já produzida pelo Livro Mestre; somente arquivos novos ou alterados usarão OCR novamente...";
            OperationStatus = $"Conferindo SHA-256 de {files.Length} arquivo(s) contra a base mestre...";
            var suggestionResult = await BuildSuggestionsAsync(files);
            var suggestions = suggestionResult.Suggestions;
            await SaveCatalogAsync(suggestionResult);
            DisplaySuggestions(suggestions);
            Status = "ANÁLISE LOCAL E CATÁLOGO CONCLUÍDOS — nada foi renomeado ou movimentado. " +
                     "Abra e aprove cada documento. Se houver erro, clique em “Refazer tudo”.";
            OperationStatus = $"Análise concluída: {suggestionResult.CacheHits} leitura(s) reaproveitada(s), " +
                              $"{suggestionResult.FreshAnalyses} nova(s) e catálogo com {suggestionResult.Analyses.Count} item(ns).";
        }
        catch (Exception exception)
        {
            Status = "A análise de conteúdo foi interrompida.";
            MessageBox.Show(exception.Message, "Análise local", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private FileItem[] GetListedFiles() => Items
        .Where(item => !item.IsExactDuplicate)
        .Select(item => new FileItem(item.OriginalPath, _fileSystem.GetFileLength(item.OriginalPath),
            Path.GetExtension(item.OriginalPath)))
        .ToArray();

    private async Task<SuggestionBuildResult> BuildSuggestionsAsync(
        IReadOnlyList<FileItem> files,
        bool forceFreshAnalysis = false)
    {
        var service = new RenameService(_fileSystem, _suggestionGateway, _documentAnalysis, new PathGuard(),
            new JsonHistoryStore(_state.RootPath!), new EmptyFolderCleaner(_fileSystem));
        if (forceFreshAnalysis)
        {
            var progress = new Progress<MasterBookProgress>(update =>
                OperationStatus = update.Total == 0
                    ? update.Message
                    : $"{update.Message} ({update.Current}/{update.Total})");
            var fresh = await _documentAnalysis.AnalyzeAsync(files, progress: progress);
            var regenerated = await service.GenerateNamesFromAnalysesAsync(fresh, explicitGenerationRequest: true);
            return new(regenerated, fresh, 0, fresh.Count);
        }
        var cache = await new MasterBookAnalysisCache(_fileSystem, _hash)
            .LoadValidAsync(_state.RootPath!, files);
        var freshAnalyses = cache.FilesRequiringAnalysis.Count == 0
            ? []
            : await _documentAnalysis.AnalyzeAsync(cache.FilesRequiringAnalysis);
        var analysesByPath = cache.CachedAnalyses.Concat(freshAnalyses)
            .ToDictionary(item => item.File.FullPath, StringComparer.OrdinalIgnoreCase);
        var analyses = files.Select(file => analysesByPath[file.FullPath]).ToArray();
        var suggestions = await service.GenerateNamesFromAnalysesAsync(analyses, explicitGenerationRequest: true);
        return new(suggestions, analyses, cache.CacheHits, freshAnalyses.Count);
    }

    private async Task SaveCatalogAsync(SuggestionBuildResult result)
    {
        var catalog = new DocumentCatalogService(_fileSystem, new JsonDocumentCatalogStore(_state.RootPath!));
        await catalog.SaveAsync(_state.RootPath!, result.Analyses, result.Suggestions);
        OperationStatus = $"Catálogo local atualizado em .organiza\\catalogo_documental.json ({result.Analyses.Count} documento(s)).";
    }

    private void DisplaySuggestions(IReadOnlyList<RenameSuggestion> suggestions)
    {
        var duplicateItems = Items.Where(item => item.IsExactDuplicate).ToArray();
        var displayItems = new List<RenameItemViewModel>(suggestions.Count + duplicateItems.Length);
        foreach (var suggestion in suggestions)
        {
            var fitted = FitSuggestionToPathLimit(suggestion);
            var item = new RenameItemViewModel(fitted.OriginalPath, fitted.SuggestedName, fitted.Reason,
                fitted.DestinationFolder, fitted.ClassificationRule, fitted.DocumentType,
                fitted.NamingProtocolStatus, fitted.QualityStatus,
                principalId: fitted.PrincipalId, secondaryId: fitted.SecondaryId) { IsSelected = true };
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RenameItemViewModel.IsReviewApproved))
                    _applyCommand.NotifyCanExecuteChanged();
            };
            displayItems.Add(item);
        }
        displayItems.AddRange(duplicateItems);
        Items.ReplaceRange(displayItems);
        SuggestionsReady = true;
        _state.MarkSuggestionsReady();
    }

    private sealed record SuggestionBuildResult(
        IReadOnlyList<RenameSuggestion> Suggestions,
        IReadOnlyList<DocumentAnalysis> Analyses,
        int CacheHits,
        int FreshAnalyses);

    private async Task ApplyAsync()
    {
        var pendingReview = Items
            .Where(item => !item.IsExactDuplicate && !item.IsReviewApproved)
            .ToArray();
        if (pendingReview.Length > 0)
        {
            Status = $"APLICAÇÃO BLOQUEADA — ainda faltam {pendingReview.Length} documento(s) para marcar como ✓ Certo.";
            MessageBox.Show(
                $"Ainda faltam {pendingReview.Length} documento(s) para conferir.\n\n" +
                "Na coluna CONFERIR, clique no botão ✓ de cada documento correto. " +
                "O botão muda de cinza para verde. Se algum nome estiver errado, use Editar ou Refazer.\n\n" +
                "Nenhum arquivo foi alterado.",
                "Conferência ainda incompleta", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = Items.Where(item => item.IsSelected && !item.IsExactDuplicate).ToArray();
        if (selected.Length == 0)
        {
            Status = "APLICAÇÃO BLOQUEADA — nenhum documento aprovado está selecionado.";
            MessageBox.Show("Nenhum documento aprovado está selecionado. Nenhum arquivo foi alterado.",
                "Nada para aplicar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var protectedRoot = _state.RootPath!;
        RenameSuggestion[] approvedSuggestions;
        try
        {
            approvedSuggestions = selected.Select(item => FitSuggestionToPathLimit(
                new RenameSuggestion(item.OriginalPath, item.SuggestedName, item.IsSelected, item.Reason,
                    item.DestinationFolder, item.ClassificationRule, item.DocumentType,
                    item.NamingProtocolStatus, item.QualityStatus,
                    item.PrincipalId, item.SecondaryId))).ToArray();
            for (var index = 0; index < selected.Length; index++)
                selected[index].SuggestedName = approvedSuggestions[index].SuggestedName;
            var assessments = approvedSuggestions
                .Select(item => new PathGuard().Assess(BuildDestinationForAssessment(
                    item, protectedRoot, _state.OrganizationMode)))
                .ToArray();
            var blockedCount = assessments.Count(result => result.Risk == PathRisk.Blocked);
            if (blockedCount > 0)
            {
                MessageBox.Show(
                    $"Foram encontrados {blockedCount} caminho(s) acima do limite máximo de 240 caracteres. " +
                    "Nenhuma alteração foi feita e a pasta raiz não será renomeada. " +
                    "Reduza manualmente os nomes sugeridos ou mova o dossiê para um caminho mais curto.",
                    "Caminho longo — lote bloqueado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var warningCount = assessments.Count(result => result.Risk == PathRisk.Warning);
            if (warningCount > 0 && MessageBox.Show(
                    $"Atenção: {warningCount} caminho(s) terão mais de 220 caracteres, embora permaneçam dentro do limite de 240. Deseja continuar?",
                    "Caminho longo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Revise os nomes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var structureMessage = _state.OrganizationMode == Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure
            ? ", criar somente as categorias necessárias e consolidar pastas padrão equivalentes"
            : string.Empty;
        if (MessageBox.Show($"Renomear {selected.Length} arquivo(s) selecionado(s){structureMessage} e remover pastas vazias? Nenhum arquivo será sobrescrito e a pasta raiz será preservada.", "Confirmação obrigatória",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            OperationStatus = $"Renomeando {selected.Length} arquivo(s) selecionado(s)...";
            Status = "Aplicando somente os nomes previamente revisados e marcados...";
            var service = new RenameService(_fileSystem, _suggestionGateway, _documentAnalysis, new PathGuard(),
                new JsonHistoryStore(protectedRoot), new EmptyFolderCleaner(_fileSystem));
            var progress = new Progress<string>(message => OperationStatus = message);
            var result = await Task.Run(() => service.ApplySelectedAsync(approvedSuggestions,
                ExplicitApproval.Grant("Usuário confirmou os itens marcados"), protectedRoot,
                _state.OrganizationMode, progress));
            if (result.LockedFiles.Count > 0)
            {
                var lockedPaths = result.LockedFiles.Select(item => item.Path)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var pending = approvedSuggestions.Where(item => lockedPaths.Contains(item.OriginalPath)).ToArray();
                Items.Clear();
                foreach (var item in pending)
                    Items.Add(new(item.OriginalPath, item.SuggestedName, item.Reason, item.DestinationFolder)
                        { IsSelected = true, IsReviewApproved = true, ReviewStatus = "✓ Certo (arquivo bloqueado)" });
                SuggestionsReady = true;
                _state.MarkSuggestionsReady();
                LockedFilesAlert = string.Join(Environment.NewLine + Environment.NewLine,
                    result.LockedFiles.Select(item => item.Message));
                Status = $"Lote parcial concluído: {result.Completed} arquivo(s) livre(s) processado(s) e {result.LockedFiles.Count} bloqueado(s) mantido(s) como pendentes. Feche os arquivos indicados e clique novamente em CONFIRMAR E RENOMEAR AGORA.";
                OperationStatus = "Arquivos bloqueados foram ignorados sem interromper os demais; tentativa disponível novamente.";
                MessageBox.Show(LockedFilesAlert,
                    "Arquivos abertos ou bloqueados", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                _state.MarkRenameApplied();
                await LoadItemsAsync();
                Status = $"Aplicação física finalizada: pasta raiz preservada, {result.Completed} arquivo(s) renomeado(s)/movido(s), {result.Skipped} já estava(m) com nome e pasta adequados e {result.Failed} falha(s). Índice atualizado em {result.MappingIndexPath}. Prossiga para o Histórico.";
                OperationStatus = result.Failed == 0
                    ? "Renomeação física concluída e registrada."
                    : "Lote concluído com falhas isoladas; os demais arquivos foram processados.";
            }
        }
        catch (Exception exception)
        {
            Status = "A renomeação foi interrompida.";
            MessageBox.Show(exception.Message, "Renomeação bloqueada", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildDestinationForAssessment(
        RenameSuggestion suggestion,
        string rootPath,
        Organiza.Domain.Selection.FolderOrganizationMode organizationMode)
    {
        var directory = organizationMode == Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure &&
                        !string.IsNullOrWhiteSpace(suggestion.DestinationFolder)
            ? Path.Combine(rootPath, suggestion.DestinationFolder)
            : Path.GetDirectoryName(suggestion.OriginalPath)!;
        var safeName = RenameService.EnforceNamingRules(
            suggestion.SuggestedName, Path.GetFileName(suggestion.OriginalPath));
        return Path.Combine(directory, PathNameShortener.FitFileName(directory, safeName));
    }

    private RenameSuggestion FitSuggestionToPathLimit(RenameSuggestion suggestion)
    {
        var directory = _state.OrganizationMode == Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure &&
                        !string.IsNullOrWhiteSpace(suggestion.DestinationFolder)
            ? Path.Combine(_state.RootPath!, suggestion.DestinationFolder)
            : Path.GetDirectoryName(suggestion.OriginalPath)!;
        var safeName = RenameService.EnforceNamingRules(
            suggestion.SuggestedName, Path.GetFileName(suggestion.OriginalPath));
        var fitted = PathNameShortener.FitFileName(directory, safeName);
        if (string.Equals(fitted, safeName, StringComparison.Ordinal))
            return suggestion with { SuggestedName = fitted };
        return suggestion with
        {
            SuggestedName = fitted,
            Reason = $"{suggestion.Reason} Nome encurtado automaticamente para manter o caminho completo em até 240 caracteres.".Trim()
        };
    }

    private async Task<bool> ValidateContextBeforeAnalysisAsync(bool recursive)
    {
        OperationStatus = "Validando o contexto da árvore em segundo plano...";
        var validation = await Task.Run(() => new WorkspacePreAnalysisService(_fileSystem)
            .Inspect(_state.RootPath!, recursive));
        _state.LastValidation = validation;
        if (!validation.HasContextContamination) return true;
        Status = $"Pré-análise bloqueou a leitura: {validation.ContextMismatches.Count} arquivo(s) parecem pertencer a outro contexto.";
        OperationStatus = "Separe fisicamente os documentos indicados e tente novamente.";
        MessageBox.Show(WorkspacePreAnalysisService.BuildOperatorMessage(validation),
            "Análise bloqueada por cruzamento de dossiês", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

}
