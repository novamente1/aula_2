using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Organiza.Application.Services;
using Organiza.Application.Abstractions;
using Organiza.Domain.Selection;
using Organiza.Domain.Operations;
using Organiza.Infrastructure.Files;
using Organiza.Infrastructure.History;
using Organiza.Wpf.Mvvm;
using Organiza.Wpf.Services;
using Forms = System.Windows.Forms;

namespace Organiza.Wpf.ViewModels;

public sealed class StructureDiagnosticRowViewModel(StructureDiagnosticItem item)
{
    public string ExpectedName => item.ExpectedName;
    public string State => item.State switch
    {
        OfficialStructureState.Confirmed => "Confirmado",
        OfficialStructureState.ReadyModel => "Modelo pronto",
        OfficialStructureState.Skeleton => "Esqueleto",
        OfficialStructureState.Empty => "Vazio",
        _ => "Incompleto"
    };
    public string Status => item.Status switch
    {
        StructureDiagnosticStatus.Found => "Encontrada",
        StructureDiagnosticStatus.Recognized => "Reconhecida",
        StructureDiagnosticStatus.Missing => "Ausente",
        _ => "Divergência"
    };
    public string FoundName => item.FoundName;
    public string Details => item.Details;
    public string PossibleAction => item.PossibleAction;
}

public sealed class FolderSelectionViewModel : ObservableObject
{
    private readonly WorkspaceState _state;
    private readonly IFileSystem _fileSystem;
    private readonly StandardFolderManager _folders;
    private readonly WorkspacePreAnalysisService _preAnalysis;
    private readonly WorkspaceComplianceAuditService _complianceAudit;
    private readonly GoogleDriveFolderLinkResolver _driveLinks;
    private readonly AsyncRelayCommand _confirmCommand;
    private readonly AsyncRelayCommand _applyTemplateCommand;
    private string _folderPath = string.Empty;
    private bool _includeSubfolders;
    private bool _applyStandardStructure;
    private bool _isConfirmed;
    private bool _isBusy;
    private string _progressStatus = string.Empty;
    private string _auditSummary = string.Empty;
    private string _auditDetails = string.Empty;
    private string _structureSummary = string.Empty;
    private bool _hasAudit;
    private StructureTemplateOption? _selectedTemplate;
    private string _status = "Selecione uma pasta para iniciar. Nenhum arquivo será alterado.";

    public FolderSelectionViewModel(WorkspaceState state, StandardFolderManager folders, IFileSystem fileSystem,
        IHashCalculator hashCalculator, GoogleDriveFolderLinkResolver driveLinks)
    {
        _state = state;
        _fileSystem = fileSystem;
        _folders = folders;
        _preAnalysis = new WorkspacePreAnalysisService(fileSystem);
        _complianceAudit = new WorkspaceComplianceAuditService(fileSystem, hashCalculator);
        _driveLinks = driveLinks;
        BrowseCommand = new RelayCommand(Browse);
        _confirmCommand = new AsyncRelayCommand(ConfirmAsync, () => !IsBusy);
        _applyTemplateCommand = new AsyncRelayCommand(ApplySelectedTemplateAsync,
            () => !IsBusy && IsConfirmed && SelectedTemplate is not null);
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value) && IsConfirmed)
            {
                IsConfirmed = false;
                Status = "O caminho foi alterado. Confirme novamente a pasta.";
            }
        }
    }
    public bool IncludeSubfolders { get => _includeSubfolders; set => SetProperty(ref _includeSubfolders, value); }
    public bool ApplyStandardStructure
    {
        get => _applyStandardStructure;
        set
        {
            if (SetProperty(ref _applyStandardStructure, value)) OnPropertyChanged(nameof(OrganizeFilesOnly));
        }
    }
    public bool OrganizeFilesOnly
    {
        get => !ApplyStandardStructure;
        set { if (value) ApplyStandardStructure = false; }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsConfirmed { get => _isConfirmed; private set => SetProperty(ref _isConfirmed, value); }
    public string ProgressStatus
    {
        get => _progressStatus;
        private set { if (SetProperty(ref _progressStatus, value)) _state.UpdateOperation(value); }
    }
    public string AuditSummary { get => _auditSummary; private set => SetProperty(ref _auditSummary, value); }
    public string AuditDetails { get => _auditDetails; private set => SetProperty(ref _auditDetails, value); }
    public bool HasAudit { get => _hasAudit; private set => SetProperty(ref _hasAudit, value); }
    public string StructureSummary { get => _structureSummary; private set => SetProperty(ref _structureSummary, value); }
    public ObservableCollection<StructureDiagnosticRowViewModel> StructureItems { get; } = [];
    public ObservableCollection<StructureTemplateOption> CompatibleTemplates { get; } = [];
    public StructureTemplateOption? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (!SetProperty(ref _selectedTemplate, value)) return;
            _applyTemplateCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            if (value) _state.BeginOperation(string.IsNullOrWhiteSpace(ProgressStatus) ? "Validando a pasta selecionada..." : ProgressStatus);
            else _state.EndOperation();
            _confirmCommand.NotifyCanExecuteChanged();
            _applyTemplateCommand.NotifyCanExecuteChanged();
        }
    }
    public ICommand BrowseCommand { get; }
    public ICommand ConfirmCommand => _confirmCommand;
    public ICommand ApplyTemplateCommand => _applyTemplateCommand;

    public async Task LoadDiagnosticPreviewAsync(string folderPath)
    {
        FolderPath = folderPath;
        IncludeSubfolders = true;
        ApplyStandardStructure = false;
        await ConfirmAsync();
    }

    private void Browse()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Selecione a pasta de trabalho do Organiza",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) FolderPath = dialog.SelectedPath;
    }

    private async Task ConfirmAsync()
    {
        IsConfirmed = false;
        IsBusy = true;
        ProgressStatus = "Validando a pasta, a estrutura e possíveis cruzamentos de dossiês...";
        try
        {
            var enteredPath = FolderPath.Trim().Trim('"');
            if (_driveLinks.IsGoogleDriveFolderUrl(enteredPath))
            {
                var driveUrl = enteredPath;
                var resolved = _driveLinks.Resolve(driveUrl);
                if (resolved is null)
                {
                    MessageBox.Show(
                        "Link do Google Drive reconhecido. Selecione uma vez a pasta correspondente no Google Drive (G:); a associação ficará salva para os próximos usos.",
                        "Associar pasta sincronizada", MessageBoxButton.OK, MessageBoxImage.Information);
                    using var associationDialog = new Forms.FolderBrowserDialog
                    {
                        Description = "Selecione a pasta local correspondente a este link do Google Drive",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false
                    };
                    if (associationDialog.ShowDialog() != Forms.DialogResult.OK)
                    {
                        Status = "O link foi reconhecido, mas a associação com a pasta local foi cancelada.";
                        return;
                    }
                    _driveLinks.Remember(driveUrl, associationDialog.SelectedPath);
                    resolved = associationDialog.SelectedPath;
                }
                enteredPath = resolved;
                FolderPath = resolved;
                Status = "Link do Google Drive reconhecido e associado à pasta sincronizada local.";
            }

            var fullPath = Path.GetFullPath(enteredPath);
            if (!Directory.Exists(fullPath))
            {
                MessageBox.Show("A pasta informada não existe.", "Organiza", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var mode = ApplyStandardStructure
                ? FolderOrganizationMode.ApplyStandardStructure
                : FolderOrganizationMode.OrganizeFilesOnly;
            var inspection = _folders.Inspect(fullPath);
            var structureProfile = FolderStructureDetector.Detect(fullPath, inspection.ExistingFolders);
            var structureDiagnostic = new StructureDiagnosticService(_fileSystem).Diagnose(fullPath);
            StructureItems.Clear();
            foreach (var item in structureDiagnostic.Items)
                StructureItems.Add(new(item));
            StructureSummary = structureDiagnostic.Summary;
            CompatibleTemplates.Clear();
            foreach (var option in new OfficialStructureTemplateService(_fileSystem).GetCompatibleOptions(fullPath))
                CompatibleTemplates.Add(option);
            SelectedTemplate = CompatibleTemplates.FirstOrDefault();
            if (mode == FolderOrganizationMode.ApplyStandardStructure &&
                FolderStructureDetector.ProtectsExistingStructure(structureProfile))
            {
                mode = FolderOrganizationMode.OrganizeFilesOnly;
                ApplyStandardStructure = false;
                Status = $"Estrutura reconhecida e protegida: {FolderStructureDetector.Describe(structureProfile)}. " +
                         "O modelo de imóvel não será criado nem misturado nesta pasta; apenas os arquivos internos poderão ser revisados.";
                MessageBox.Show(Status, "Modelo de pasta protegido", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (mode == FolderOrganizationMode.ApplyStandardStructure && !inspection.HasAllStandardFolders)
            {
                var choice = MessageBox.Show(
                    $"Aplicar especificamente o MODELO DE IMÓVEL nesta pasta?\n\n{fullPath}\n\n" +
                    "São 10 módulos: 01 PESQUISA, 02 ARREMATAÇÃO, 03 CARTORIO E REGISTRO, " +
                    "04 REFORMA E PUBLICIDADE, 05 IPTU, 06 LOCACAO, 07 ACOES, " +
                    "07A PROCESSOS ADMINISTRATIVOS, 08 CONTRATOS e 09 FOTOS.\n\n" +
                    "Escolha SIM somente para um dossiê de imóvel. Nesta etapa nada será criado ou movido. " +
                    "Se escolher NÃO, o aplicativo preservará integralmente a estrutura existente.",
                    "Confirmar o modelo correto",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (choice == MessageBoxResult.Yes)
                {
                    Status = "Pasta selecionada; estrutura padrão programada para a Etapa 3. Nenhum arquivo foi movido na confirmação.";
                }
                else
                {
                    mode = FolderOrganizationMode.OrganizeFilesOnly;
                    ApplyStandardStructure = false;
                    Status = "Modo somente arquivos: nenhuma pasta padrão foi criada e a raiz será preservada.";
                }
            }
            else if (mode == FolderOrganizationMode.ApplyStandardStructure)
            {
                Status = $"Estrutura reconhecida: {FolderStructureDetector.Describe(structureProfile)}. " +
                         "O modelo será reutilizado somente na Etapa 3; nenhuma movimentação ocorreu agora.";
            }
            else
            {
                Status = $"Estrutura reconhecida: {FolderStructureDetector.Describe(structureProfile)}. " +
                         "Modo somente arquivos: estrutura e nome da pasta raiz serão preservados.";
            }

            _state.ConfirmSelection(fullPath, IncludeSubfolders, mode);
            ProgressStatus = "Inventariando subpastas em segundo plano...";
            var validation = await Task.Run(() => _preAnalysis.Inspect(fullPath, IncludeSubfolders));
            _state.LastValidation = validation;
            if (validation.HasContextContamination)
            {
                Status += $" ALERTA: {validation.ContextMismatches.Count} arquivo(s) de contexto divergente; a Etapa 3 ficará bloqueada até a separação física.";
                MessageBox.Show(WorkspacePreAnalysisService.BuildOperatorMessage(validation),
                    "Possível cruzamento de dossiês", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (validation.EquivalentFolderGroups.Count > 0)
            {
                Status += $" {validation.EquivalentFolderGroups.Count} grupo(s) de pastas equivalentes precisa(m) de consolidação no modo estrutura padrão.";
            }

            ProgressStatus = "Inventariando estrutura e documentos; hashes completos ficam para a Etapa 2...";
            var audit = await Task.Run(() => _complianceAudit.AuditAsync(fullPath,
                mode == FolderOrganizationMode.ApplyStandardStructure, includeContentHashes: false));
            AuditSummary = audit.Summary;
            AuditDetails = audit.RenderDetails();
            HasAudit = true;
            if (!audit.IsCompliant)
                Status += $" Auditoria automática: {audit.CriticalCount} divergência(s) crítica(s) e {audit.WarningCount} alerta(s).";

            IsConfirmed = true;
            Status = $"Pasta confirmada com sucesso. Prossiga para a próxima etapa. {Status}";
            ProgressStatus = "Validação concluída. A próxima etapa foi liberada.";
        }
        catch (Exception exception)
        {
            IsConfirmed = false;
            MessageBox.Show(exception.Message, "Não foi possível selecionar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplySelectedTemplateAsync()
    {
        if (!_state.HasSelection || SelectedTemplate is null) return;
        var selected = SelectedTemplate;
        if (MessageBox.Show(
                $"Criar somente as pastas ausentes do modelo “{selected.DisplayName}”?\n\n" +
                $"{selected.Explanation}\n\nA pasta raiz não será renomeada e nenhum arquivo será movido, renomeado ou excluído.",
                "Confirmação obrigatória da estrutura", MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes) return;

        IsBusy = true;
        ProgressStatus = $"Aplicando o modelo confirmado: {selected.DisplayName}...";
        try
        {
            var service = new OfficialStructureTemplateService(_fileSystem);
            var result = service.Apply(_state.RootPath!, selected.Template,
                ExplicitApproval.Grant("Usuário confirmou a criação das pastas ausentes do modelo oficial"));
            var history = new JsonHistoryStore(_state.RootPath!);
            await history.AppendAsync(result.Created.Select(path => new OperationLogEntry(
                DateTimeOffset.Now, "Criar pasta estrutural oficial", _state.RootPath!, path,
                Organiza.Domain.Operations.OperationStatus.Completed,
                $"Modelo {selected.Template}; criação explicitamente aprovada. Nenhum documento foi alterado.")));

            var diagnostic = new StructureDiagnosticService(_fileSystem).Diagnose(_state.RootPath!);
            StructureItems.Clear();
            foreach (var item in diagnostic.Items) StructureItems.Add(new(item));
            StructureSummary = diagnostic.Summary;
            var audit = await Task.Run(() => _complianceAudit.AuditAsync(_state.RootPath!,
                _state.OrganizationMode == FolderOrganizationMode.ApplyStandardStructure,
                includeContentHashes: false));
            AuditSummary = audit.Summary;
            AuditDetails = audit.RenderDetails();
            Status = $"Estrutura aplicada com aprovação: {result.Created.Count} pasta(s) criada(s) e " +
                     $"{result.Reused.Count} existente(s) reutilizada(s). A raiz e os documentos foram preservados.";
            ProgressStatus = "Estrutura atualizada e diagnóstico refeito.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Estrutura não aplicada", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
