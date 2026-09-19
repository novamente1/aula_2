using System.Text.RegularExpressions;
using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;
using Organiza.Domain.Selection;

namespace Organiza.Application.Services;

public sealed partial class WorkspacePreAnalysisService(IFileSystem fileSystem)
{
    public WorkspaceValidationResult Inspect(string rootPath, bool recursive)
    {
        var root = Path.GetFullPath(rootPath);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = fileSystem.EnumerateFiles(root, option)
            .Where(path => !WorkspaceFilePolicy.IsGeneratedArtifact(path) &&
                           !WorkspaceFilePolicy.IsGoogleWorkspacePointer(path))
            .ToArray();
        var rootProcesses = ExtractProcesses(Path.GetFileName(root));
        var ordinaryFiles = files.Where(path =>
                !WorkspaceFilePolicy.IsInsideOperationalFolder(root, path) &&
                !WorkspaceFilePolicy.IsGeneratedPdfPart(path))
            .ToArray();
        var processCounts = ordinaryFiles
            .SelectMany(path => ExtractProcesses(Path.GetFileName(path)))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var primary = rootProcesses.FirstOrDefault() ?? processCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        var mismatches = new List<ContextMismatchCandidate>();
        if (rootProcesses.Length > 0)
        {
            foreach (var path in files.Where(path => !WorkspaceFilePolicy.IsGeneratedPdfPart(path)))
            {
                foreach (var process in ExtractProcesses(Path.GetFileName(path)))
                {
                    if (rootProcesses.Contains(process, StringComparer.OrdinalIgnoreCase)) continue;
                    mismatches.Add(new(path, $"Processo {process}",
                        string.Join("; ", rootProcesses.Select(value => $"Processo {value}")),
                        "O número processual do arquivo não consta entre os processos declarados no nome da pasta raiz."));
                }
            }
        }

        var equivalentGroups = StandardFolders.All.Select(canonical =>
            {
                var matches = fileSystem.EnumerateDirectories(root)
                    .Where(path => StandardFolderManager.IsEquivalentFolder(Path.GetFileName(path), canonical))
                    .ToArray();
                return new EquivalentFolderGroup(canonical, matches);
            })
            .Where(group => group.FolderPaths.Count > 1)
            .ToArray();
        var emptyFolders = new EmptyFolderCleaner(fileSystem).FindEmptyFolders(root);
        return new(primary is null ? null : $"Processo {primary}",
            mismatches.DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            equivalentGroups,
            emptyFolders);
    }

    public static string BuildOperatorMessage(WorkspaceValidationResult result)
    {
        if (!result.HasContextContamination) return "Nenhum cruzamento processual foi detectado pelos identificadores dos arquivos.";
        var lines = result.ContextMismatches.Take(15)
            .Select(item => $"• {Path.GetFileName(item.FullPath)} — {item.DetectedContext}")
            .ToList();
        if (result.ContextMismatches.Count > lines.Count)
            lines.Add($"• ... e mais {result.ContextMismatches.Count - lines.Count} arquivo(s)");
        return $"Contexto esperado: {result.PrimaryContext ?? "não identificado"}.\n\n" +
               "Mova fisicamente os arquivos abaixo para o dossiê correto antes da análise integral:\n" +
               string.Join(Environment.NewLine, lines);
    }

    private static string[] ExtractProcesses(string value) => ProcessNumberRegex().Matches(value)
        .Cast<Match>().Select(match => NormalizeProcess(match.Value))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NormalizeProcess(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 20
            ? $"{digits[..7]}-{digits.Substring(7, 2)}.{digits.Substring(9, 4)}.{digits.Substring(13, 1)}.{digits.Substring(14, 2)}.{digits.Substring(16, 4)}"
            : value;
    }

    [GeneratedRegex(@"(?<!\d)(?:\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}|\d{20})(?!\d)")]
    private static partial Regex ProcessNumberRegex();
}
