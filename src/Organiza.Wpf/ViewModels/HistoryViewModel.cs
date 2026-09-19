using System.Collections.ObjectModel;
using System.Text;
using Organiza.Domain.Operations;
using Organiza.Infrastructure.History;
using Organiza.Wpf.Mvvm;
using Organiza.Wpf.Services;

namespace Organiza.Wpf.ViewModels;

public sealed record HistoryEntryDisplay(
    DateTimeOffset Timestamp,
    string Action,
    string Description,
    string Result);

public sealed class HistoryViewModel(WorkspaceState state) : ObservableObject, IActivatablePage
{
    private string _status = "O histórico será apresentado como relatório de execução.";
    private string _latestReport = "Nenhuma execução concluída nesta pasta.";
    private CancellationTokenSource? _loadCancellation;
    public ObservableCollection<HistoryEntryDisplay> Entries { get; } = [];
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string LatestReport { get => _latestReport; private set => SetProperty(ref _latestReport, value); }

    public async void Activate()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        Entries.Clear();
        if (!state.HasSelection)
        {
            Status = "Selecione uma pasta na etapa 1.";
            return;
        }

        try
        {
            var entries = (await new JsonHistoryStore(state.RootPath!).ReadAsync(cancellationToken))
                .OrderByDescending(entry => entry.Timestamp).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in entries)
                Entries.Add(new(entry.Timestamp, FriendlyAction(entry.Operation), Describe(entry, state.RootPath!),
                    FriendlyResult(entry.Status)));

            LatestReport = BuildLatestReport(entries, state.RootPath!);
            SaveInternalReport(state.RootPath!, LatestReport);
            Status = entries.Length == 0
                ? "Ainda não há operações registradas."
                : $"{entries.Length} evento(s) registrados. O quadro abaixo mostra o que foi alterado, mantido e gerado.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Status = $"Não foi possível ler o histórico: {exception.Message}";
        }
    }

    private static string BuildLatestReport(IReadOnlyList<OperationLogEntry> ordered, string root)
    {
        if (ordered.Count == 0) return "Nenhuma execução concluída nesta pasta.";
        var newest = ordered[0].Timestamp;
        var latest = ordered.Where(entry => newest - entry.Timestamp <= TimeSpan.FromMinutes(15)).ToArray();
        var completed = latest.Count(entry => entry.Status == OperationStatus.Completed);
        var unchanged = latest.Count(entry => entry.Status == OperationStatus.Skipped);
        var failed = latest.Count(entry => entry.Status == OperationStatus.Failed);
        var renamed = latest.Count(entry => entry.Operation.Contains("Renomear", StringComparison.OrdinalIgnoreCase) && entry.Status == OperationStatus.Completed);
        var moved = latest.Count(entry => entry.Destination is not null && entry.Status == OperationStatus.Completed);
        var removed = latest.Count(entry => entry.Operation.Contains("pasta vazia", StringComparison.OrdinalIgnoreCase) && entry.Status == OperationStatus.Completed);
        var generated = latest.Count(entry => entry.Operation.Contains("Livro Mestre", StringComparison.OrdinalIgnoreCase) || entry.Operation.Contains("Dividir PDF", StringComparison.OrdinalIgnoreCase));
        return $"ÚLTIMA EXECUÇÃO — {newest:dd/MM/yyyy HH:mm:ss}\n" +
               $"Resultado: {completed} alteração(ões) física(s), {unchanged} item(ns) que já estava(m) adequado(s) e {failed} falha(s).\n" +
               $"Alterações: {renamed} arquivo(s) renomeado(s), {moved} movimentação(ões), {removed} pasta(s) vazia(s) removida(s) e {generated} artefato(s)/divisão(ões) gerado(s).\n" +
               "Cada linha abaixo informa a origem, o destino e o resultado físico. O relatório também foi salvo na área interna .organiza.";
    }

    private static string Describe(OperationLogEntry entry, string root)
    {
        var source = Relative(root, entry.Source);
        var destination = string.IsNullOrWhiteSpace(entry.Destination) ? null : Relative(root, entry.Destination!);
        var change = destination is null ? source : $"{source}  →  {destination}";
        return string.IsNullOrWhiteSpace(entry.Details) ? change : $"{change}. {entry.Details}";
    }

    private static string Relative(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    private static string FriendlyAction(string operation) => operation switch
    {
        "Renomear arquivo" => "Renomeação / organização",
        "Mover duplicado" => "Duplicidade movida para 98",
        "Remover pasta vazia" => "Pasta vazia removida",
        "Unificar pasta similar" => "Pasta consolidada",
        _ => operation
    };

    private static string FriendlyResult(OperationStatus status) => status switch
    {
        OperationStatus.Completed => "Concluído",
        OperationStatus.Skipped => "Já adequado",
        OperationStatus.Failed => "Falhou",
        OperationStatus.Blocked => "Bloqueado",
        _ => "Planejado"
    };

    private static void SaveInternalReport(string root, string report)
    {
        try
        {
            var directory = Path.Combine(root, ".organiza");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "RELATORIO-ULTIMA-EXECUCAO.md");
            File.WriteAllText(path, $"# Relatório da última execução\n\n{report.Replace("\n", "\n\n")}", new UTF8Encoding(true));
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch { }
    }
}
