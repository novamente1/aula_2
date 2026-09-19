using System.Collections.ObjectModel;
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

public sealed class PdfCandidateViewModel(string fullPath, long sizeBytes) : ObservableObject
{
    private bool _isSelected;
    public string FullPath { get; } = fullPath;
    public string Name => Path.GetFileName(FullPath);
    public long SizeBytes { get; } = sizeBytes;
    public string SizeText => $"{SizeBytes / 1024d / 1024d:N1} MB";
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class PdfSplitViewModel : ObservableObject, IActivatablePage
{
    private readonly WorkspaceState _state;
    private readonly IFileSystem _fileSystem;
    private readonly IHashCalculator _hash;
    private readonly StandardFolderManager _folders;
    private readonly PathGuard _pathGuard;
    private readonly IPdfDocumentAdapter _pdf;
    private readonly AsyncRelayCommand _splitCommand;
    private string _status = "Partes limitadas por tamanho real a aproximadamente 97 MB.";
    private string _operationStatus = string.Empty;
    private bool _isBusy;
    private double _progressValue;

    public PdfSplitViewModel(WorkspaceState state, IFileSystem fileSystem, IHashCalculator hash,
        StandardFolderManager folders, PathGuard pathGuard, IPdfDocumentAdapter pdf)
    {
        _state = state;
        _fileSystem = fileSystem;
        _hash = hash;
        _folders = folders;
        _pathGuard = pathGuard;
        _pdf = pdf;
        _splitCommand = new AsyncRelayCommand(SplitAsync, () => !IsBusy);
    }

    public ObservableCollection<PdfCandidateViewModel> Files { get; } = [];
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
            if (value) _state.BeginOperation(string.IsNullOrWhiteSpace(OperationStatus) ? "Preparando a divisão segura dos PDFs..." : OperationStatus);
            else _state.EndOperation();
            _splitCommand.NotifyCanExecuteChanged();
        }
    }
    public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }
    public ICommand SplitCommand => _splitCommand;

    public void Activate()
    {
        Files.Clear();
        if (!_state.HasSelection) return;
        try
        {
            var option = _state.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var inaccessibleFiles = 0;
            var oversizedGeneratedParts = 0;
            var oversizedProtectedFiles = 0;
            foreach (var path in _fileSystem.EnumerateFiles(_state.RootPath!, option)
                         .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var size = _fileSystem.GetFileLength(path);
                    if (size <= PdfSafetyLimits.PartSizeBytes) continue;
                    if (WorkspaceFilePolicy.IsPdfSplitCandidate(_state.RootPath!, path))
                        Files.Add(new(path, size));
                    else if (WorkspaceFilePolicy.IsGeneratedPdfPart(path))
                        oversizedGeneratedParts++;
                    else if (WorkspaceFilePolicy.IsInsideOperationalFolder(_state.RootPath!, path))
                        oversizedProtectedFiles++;
                }
                catch (IOException)
                {
                    inaccessibleFiles++;
                }
                catch (UnauthorizedAccessException)
                {
                    inaccessibleFiles++;
                }
            }

            Status = Files.Count == 0
                ? "Nenhum PDF acima de 97 MB encontrado."
                : $"{Files.Count} PDF(s) grande(s). Todos começam desmarcados.";
            if (oversizedGeneratedParts > 0)
                Status += $" {oversizedGeneratedParts} parte(s) já gerada(s) acima do limite foram protegidas contra reprocessamento em ciclo.";
            if (oversizedProtectedFiles > 0)
                Status += $" {oversizedProtectedFiles} PDF(s) grande(s) em 98 DUPLICADOS/99 ORIGINAIS permanecem preservados e não são listados.";
            if (inaccessibleFiles > 0)
                Status += $" {inaccessibleFiles} arquivo(s) temporariamente inacessível(is) foram ignorados.";
        }
        catch (Exception exception)
        {
            Status = "Não foi possível concluir a leitura da pasta, mas o aplicativo permaneceu aberto.";
            MessageBox.Show(
                "Não foi possível listar os PDFs. O Google Drive pode estar sincronizando ou bloqueando a pasta.\n\n" +
                $"Detalhe: {exception.Message}",
                "Falha ao acessar a pasta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SplitAsync()
    {
        var selected = Files.Where(file => file.IsSelected).ToArray();
        if (selected.Length == 0) return;
        var originalsDirectory = Path.Combine(_state.RootPath!, "99 ORIGINAIS");
        var pathAssessments = selected.SelectMany(file =>
            {
                var partDirectory = Path.GetDirectoryName(file.FullPath)!;
                return new[]
                {
                    Path.Combine(originalsDirectory, PathNameShortener.FitFileName(originalsDirectory, file.Name)),
                    Path.Combine(partDirectory, PathNameShortener.FitFileName(partDirectory,
                        $"{Path.GetFileNameWithoutExtension(file.Name)}_parte-01.pdf"))
                };
            })
            .Select(_pathGuard.Assess)
            .ToArray();
        var blockedCount = pathAssessments.Count(result => result.Risk == PathRisk.Blocked);
        if (blockedCount > 0)
        {
            MessageBox.Show(
                $"A divisão foi bloqueada porque {blockedCount} caminho(s) previsto(s) ultrapassam o limite máximo de 240 caracteres. " +
                "Nenhum arquivo foi alterado. Reduza manualmente o nome do PDF ou mova o dossiê para um caminho mais curto.",
                "Caminho longo — divisão bloqueada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var warningCount = pathAssessments.Count(result => result.Risk == PathRisk.Warning);
        if (warningCount > 0 && MessageBox.Show(
                $"Atenção: {warningCount} caminho(s) previstos terão mais de 220 caracteres, mas ficarão dentro do limite de 240. Deseja continuar?",
                "Caminho longo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var answer = MessageBox.Show(
            $"Dividir {selected.Length} PDF(s)? Os originais serão preservados em 99 ORIGINAIS e pastas vazias não estruturais poderão ser removidas.",
            "Confirmar divisão", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        IsBusy = true;
        ProgressValue = 0;
        try
        {
            var history = new JsonHistoryStore(_state.RootPath!);
            var cleaner = new EmptyFolderCleaner(_fileSystem);
            var service = new PdfSplitService(_fileSystem, _hash, _pdf, _folders, _pathGuard, history, cleaner);
            var failures = new List<string>();
            var successCount = 0;
            for (var index = 0; index < selected.Length; index++)
            {
                var item = selected[index];
                OperationStatus = $"Processando PDF {index + 1} de {selected.Length}: {item.Name}";
                Status = "Medindo partes pelo tamanho real e preservando o original...";
                try
                {
                    await Task.Run(() => service.SplitAsync(item.FullPath, _state.RootPath!,
                        ExplicitApproval.Grant("Usuário confirmou a divisão")));
                    successCount++;
                }
                catch (Exception exception)
                {
                    failures.Add($"{item.Name}: {exception.Message}");
                }
                ProgressValue = (index + 1d) / selected.Length * 100d;
            }
            Activate();
            if (failures.Count == 0)
            {
                Status = "Divisão concluída; originais conferidos e preservados. Prossiga para o Histórico para conferir as partes criadas.";
                OperationStatus = "Processamento concluído e próxima fase liberada.";
            }
            else
            {
                Status = $"Processamento concluído com {successCount} sucesso(s) e {failures.Count} falha(s). O aplicativo permaneceu estável.";
                OperationStatus = "Consulte a mensagem e o histórico para os detalhes das falhas.";
                MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, failures),
                    "Alguns PDFs não puderam ser divididos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            Status = "A divisão foi interrompida.";
            MessageBox.Show(exception.Message, "Falha ao dividir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
