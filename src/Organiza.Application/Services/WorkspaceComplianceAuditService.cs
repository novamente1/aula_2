using System.Text;
using System.Text.RegularExpressions;
using Organiza.Application.Abstractions;
using Organiza.Domain.MasterBook;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public enum ComplianceSeverity
{
    Information,
    Warning,
    Critical
}

public sealed record ComplianceFinding(
    string Code,
    ComplianceSeverity Severity,
    string Title,
    string Recommendation,
    IReadOnlyList<string> Examples);

public sealed record WorkspaceComplianceAuditResult(
    string RootPath,
    int FileCount,
    int DirectoryCount,
    long TotalBytes,
    int DuplicateGroups,
    int DuplicateFiles,
    IReadOnlyList<ComplianceFinding> Findings)
{
    public int CriticalCount => Findings.Count(item => item.Severity == ComplianceSeverity.Critical);
    public int WarningCount => Findings.Count(item => item.Severity == ComplianceSeverity.Warning);
    public bool IsCompliant => CriticalCount == 0 && WarningCount == 0;

    public string Summary => IsCompliant
        ? $"Conforme: {FileCount} arquivo(s) e {DirectoryCount} pasta(s) auditados."
        : $"Auditoria: {CriticalCount} divergência(s) crítica(s), {WarningCount} alerta(s), " +
          $"{FileCount} arquivo(s) e {DirectoryCount} pasta(s).";

    public string RenderDetails()
    {
        if (Findings.Count == 0) return "Nenhuma divergência encontrada.";
        var builder = new StringBuilder();
        foreach (var finding in Findings.OrderByDescending(item => item.Severity))
        {
            var label = finding.Severity switch
            {
                ComplianceSeverity.Critical => "CRÍTICO",
                ComplianceSeverity.Warning => "ALERTA",
                _ => "INFO"
            };
            builder.AppendLine($"[{label}] {finding.Title}");
            builder.AppendLine($"Ação: {finding.Recommendation}");
            foreach (var example in finding.Examples.Take(5)) builder.AppendLine($"• {example}");
            if (finding.Examples.Count > 5) builder.AppendLine($"• ... e mais {finding.Examples.Count - 5}");
            builder.AppendLine();
        }
        return builder.ToString().TrimEnd();
    }
}

public sealed partial class WorkspaceComplianceAuditService(
    IFileSystem fileSystem,
    IHashCalculator hashCalculator)
{
    private const long PdfLimit = 97L * 1024 * 1024;

    public async Task<WorkspaceComplianceAuditResult> AuditAsync(
        string rootPath,
        bool expectStandardStructure = false,
        bool includeContentHashes = true,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootPath);
        var allFiles = fileSystem.EnumerateFiles(root, SearchOption.AllDirectories).ToArray();
        var googleWorkspacePointers = allFiles.Where(WorkspaceFilePolicy.IsGoogleWorkspacePointer).ToArray();
        var files = allFiles.Where(path => !WorkspaceFilePolicy.IsGoogleWorkspacePointer(path)).ToArray();
        var directories = fileSystem.EnumerateDirectories(root, SearchOption.AllDirectories).ToArray();
        var directDirectories = fileSystem.EnumerateDirectories(root).ToArray();
        var findings = new List<ComplianceFinding>();

        if (googleWorkspacePointers.Length > 0)
        {
            findings.Add(new("GOOGLE_WORKSPACE_POINTER", ComplianceSeverity.Information,
                $"{googleWorkspacePointers.Length} atalho(s) nativo(s) do Google Workspace foram reconhecidos e ignorados com segurança.",
                "Abra esses itens no Google Drive ou exporte-os para PDF/DOCX se quiser analisar o conteúdo. O Organiza não tenta medir, gerar hash, aplicar OCR ou renomear arquivos .gdoc/.gsheet/.gslides.",
                googleWorkspacePointers.Select(path => Relative(root, path)).ToArray()));
        }

        DetectLongPaths(root, allFiles, directories, findings);

        var isPortfolioRoot = DetectPortfolioRoot(root, directDirectories, findings);
        if (isPortfolioRoot)
        {
            return new(root, allFiles.Length, directories.Length, files.Sum(fileSystem.GetFileLength),
                0, 0, findings);
        }
        if (expectStandardStructure) DetectMissingStructure(directDirectories, findings);
        DetectRootResidues(root, files, findings);
        DetectTemporaryFiles(root, files, findings);
        DetectEmptyNonStructuralFolders(root, directories, findings);
        DetectPdfPartProblems(root, files, findings);
        DetectMasterBookProblems(root, files, findings);
        DetectRootNaming(root, findings);

        var duplicateGroups = includeContentHashes
            ? await FindDuplicateGroupsAsync(files, cancellationToken)
            : [];
        if (!includeContentHashes)
        {
            findings.Add(new("DUPLICATE_SCAN_DEFERRED", ComplianceSeverity.Information,
                "A verificação profunda de duplicidades foi reservada para a Etapa 2.",
                "Use “Verificar todos agora” na Etapa 2. A seleção inicial não calcula hashes de toda a árvore, evitando travamentos em pastas grandes.", []));
        }
        var duplicateDestination = new DuplicateDestinationResolver(fileSystem).Resolve(root);
        if (duplicateGroups.Count > 0)
        {
            var unresolved = duplicateGroups.Where(group => group.Paths.Count(path =>
                    !WorkspaceFilePolicy.IsInsideDuplicatesFolder(root, path) &&
                    !WorkspaceFilePolicy.IsInsideOriginalsFolder(root, path)) > 1).ToArray();
            var quarantined = duplicateGroups.Except(unresolved).ToArray();
            if (unresolved.Length > 0)
            {
                findings.Add(new("EXACT_DUPLICATES", ComplianceSeverity.Warning,
                    $"{unresolved.Length} grupo(s) de duplicidade exata ainda possuem múltiplas cópias ativas.",
                    $"Manter um principal e mover somente as cópias aprovadas para {duplicateDestination.FolderName}; " +
                    $"regra {duplicateDestination.RuleCode}. Não excluir automaticamente.",
                    unresolved.Select(group =>
                        $"{group.Count} cópias: {string.Join(" | ", group.Paths.Take(3).Select(path => Relative(root, path)))}")
                        .ToArray()));
            }
            if (quarantined.Length > 0)
            {
                findings.Add(new("QUARANTINED_DUPLICATES", ComplianceSeverity.Information,
                    $"{quarantined.Length} grupo(s) de duplicidade estão corretamente preservados em 98/99.",
                    $"Manter como quarentena auditável em {duplicateDestination.FolderName}; nenhuma exclusão automática é necessária.",
                    quarantined.Select(group =>
                    $"{group.Count} cópias: {string.Join(" | ", group.Paths.Take(3).Select(path => Relative(root, path)))}")
                        .ToArray()));
            }
        }

        return new(root, allFiles.Length, directories.Length, files.Sum(fileSystem.GetFileLength),
            duplicateGroups.Count, duplicateGroups.Sum(group => group.Count), findings);
    }

    private static void DetectLongPaths(
        string root,
        IReadOnlyList<string> files,
        IReadOnlyList<string> directories,
        ICollection<ComplianceFinding> findings)
    {
        var longFiles = files.Where(path => Path.GetFullPath(path).Length > PathGuard.BlockingLength).ToArray();
        var longDirectories = directories.Where(path => Path.GetFullPath(path).Length > PathGuard.BlockingLength).ToArray();
        if (longFiles.Length > 0)
        {
            findings.Add(new("PATH_OVER_240_FILE", ComplianceSeverity.Critical,
                $"{longFiles.Length} arquivo(s) possuem caminho acima do limite máximo de 240 caracteres.",
                "Use Organizar e renomear: o nome será encurtado antes da confirmação, preservando extensão e identificadores reconhecidos.",
                longFiles.Select(path => $"{Path.GetFullPath(path).Length} caracteres — {Relative(root, path)}").ToArray()));
        }
        if (longDirectories.Length > 0)
        {
            findings.Add(new("PATH_OVER_240_DIRECTORY", ComplianceSeverity.Critical,
                $"{longDirectories.Length} pasta(s) possuem caminho acima de 240 caracteres.",
                "Reduza manualmente a profundidade ou o nome de uma subpasta. O Organiza nunca renomeia a raiz nem pastas personalizadas silenciosamente.",
                longDirectories.Select(path => $"{Path.GetFullPath(path).Length} caracteres — {Relative(root, path)}").ToArray()));
        }
    }

    private static bool DetectPortfolioRoot(
        string root,
        IReadOnlyList<string> directDirectories,
        ICollection<ComplianceFinding> findings)
    {
        var dossierChildren = directDirectories
            .Where(path => DossierFolderRegex().IsMatch(Path.GetFileName(path)))
            .ToArray();
        var canonicalCount = StandardFolders.All.Count(canonical => directDirectories.Any(path =>
            StandardFolderManager.IsEquivalentFolder(Path.GetFileName(path), canonical)));
        if (dossierChildren.Length < 2 || canonicalCount >= 3) return false;

        findings.Add(new("MULTIPLE_DOSSIERS_ROOT", ComplianceSeverity.Critical,
            "A pasta selecionada é um portfólio com múltiplos dossiês, não um dossiê individual.",
            "Selecione e processe separadamente cada subpasta V/P. Não gere um Livro Mestre na pasta-pai.",
            dossierChildren.Select(path => Relative(root, path)).ToArray()));
        return true;
    }

    private static void DetectMissingStructure(
        IReadOnlyList<string> directDirectories,
        ICollection<ComplianceFinding> findings)
    {
        var missing = StandardFolders.All.Where(canonical => !directDirectories.Any(path =>
            StandardFolderManager.IsEquivalentFolder(Path.GetFileName(path), canonical))).ToArray();
        if (missing.Length == 0) return;
        findings.Add(new("MISSING_CANONICAL_FOLDERS", ComplianceSeverity.Warning,
            $"{missing.Length} pasta(s) canônica(s) ausente(s).",
            "Aplicar a estrutura padrão somente dentro de um dossiê individual confirmado.", missing));
    }

    private static void DetectRootResidues(
        string root,
        IReadOnlyList<string> files,
        ICollection<ComplianceFinding> findings)
    {
        var loose = files.Where(path =>
                string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase) &&
                !WorkspaceFilePolicy.IsGeneratedArtifact(path))
            .ToArray();
        if (loose.Length == 0) return;
        findings.Add(new("LOOSE_ROOT_FILES", ComplianceSeverity.Warning,
            $"{loose.Length} documento(s) permanecem soltos na raiz do dossiê.",
            "Classificar pelo conteúdo e mover para a pasta canônica adequada após revisão dos nomes sugeridos.",
            loose.Select(path => Path.GetFileName(path)).ToArray()));
    }

    private static void DetectTemporaryFiles(
        string root,
        IReadOnlyList<string> files,
        ICollection<ComplianceFinding> findings)
    {
        var temporary = files.Where(path =>
        {
            var name = Path.GetFileName(path);
            return name.StartsWith(".$", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (temporary.Length == 0) return;
        findings.Add(new("TEMPORARY_FILES", ComplianceSeverity.Warning,
            $"{temporary.Length} arquivo(s) temporário(s) ou de lock encontrado(s).",
            "Remover locks inativos e transferir legados recuperáveis para o armazenamento interno local.",
            temporary.Select(path => Relative(root, path)).ToArray()));
    }

    private static void DetectEmptyNonStructuralFolders(
        string root,
        IReadOnlyList<string> directories,
        ICollection<ComplianceFinding> findings)
    {
        var structuralEmpty = directories.Where(path => !fileSystemStaticHasEntries(path) &&
                                                         DriveStructureCatalog.IsProtectedFolderName(Path.GetFileName(path)))
            .ToArray();
        if (structuralEmpty.Length > 0)
        {
            findings.Add(new("EMPTY_STRUCTURAL", ComplianceSeverity.Information,
                $"{structuralEmpty.Length} pasta(s) estruturais válidas estão vazias e foram mantidas intencionalmente.",
                "Nenhuma ação: categorias e módulos reconhecidos permanecem protegidos mesmo quando vazios.",
                structuralEmpty.Select(path => Relative(root, path)).ToArray()));
        }
        var empty = directories.Where(path => !fileSystemStaticHasEntries(path) &&
                                               !DriveStructureCatalog.IsProtectedFolderName(Path.GetFileName(path)) &&
                                               !WorkspaceFilePolicy.IsInsideInternalFolder(root, path))
            .ToArray();
        if (empty.Length == 0) return;
        findings.Add(new("EMPTY_NON_STRUCTURAL", ComplianceSeverity.Warning,
            $"{empty.Length} pasta(s) vazia(s) sem conteúdo útil, inclusive categorias ainda não utilizadas.",
            "Remover após confirmação e registrar no histórico.",
            empty.Select(path => Relative(root, path)).ToArray()));

        static bool fileSystemStaticHasEntries(string path) =>
            Directory.EnumerateFileSystemEntries(path).Any();
    }

    private static void DetectPdfPartProblems(
        string root,
        IReadOnlyList<string> files,
        ICollection<ComplianceFinding> findings)
    {
        var generated = files.Where(WorkspaceFilePolicy.IsGeneratedPdfPart).ToArray();
        var oversized = generated.Where(path => new FileInfo(path).Length > PdfLimit).ToArray();
        if (oversized.Length > 0)
        {
            findings.Add(new("OVERSIZED_PDF_PART", ComplianceSeverity.Critical,
                $"{oversized.Length} parte(s) de PDF excedem 97 MiB.",
                "Refazer a divisão com validação estrutural antes de remover o original de trabalho.",
                oversized.Select(path => Relative(root, path)).ToArray()));
        }

        foreach (var group in generated.GroupBy(path => GeneratedPartRegex().Replace(path, string.Empty),
                     StringComparer.OrdinalIgnoreCase))
        {
            var numbers = group.Select(path => int.Parse(GeneratedPartNumberRegex().Match(path).Groups[1].Value))
                .OrderBy(value => value).ToArray();
            if (numbers.Length == 0 || numbers.SequenceEqual(Enumerable.Range(1, numbers.Length))) continue;
            findings.Add(new("PDF_PART_SEQUENCE_GAP", ComplianceSeverity.Critical,
                "A sequência de partes de um PDF possui lacuna ou numeração residual.",
                "Não usar as partes; restaurar o original preservado e dividir novamente.",
                group.Select(path => Relative(root, path)).ToArray()));
        }
    }

    private static void DetectMasterBookProblems(
        string root,
        IReadOnlyList<string> files,
        ICollection<ComplianceFinding> findings)
    {
        var markdown = Path.Combine(root, MasterBookFileNames.Markdown);
        var json = Path.Combine(root, MasterBookFileNames.Json);
        if (File.Exists(markdown) && !File.Exists(json))
        {
            findings.Add(new("MASTER_BOOK_JSON_MISSING", ComplianceSeverity.Critical,
                "O Livro Mestre possui Markdown, mas a base JSON auditável está ausente.",
                "Regenerar o Livro Mestre 360° depois da higienização e renomeação.",
                [MasterBookFileNames.Markdown, MasterBookFileNames.Json]));
        }

        if (!File.Exists(markdown)) return;
        var generatedAt = File.GetLastWriteTimeUtc(markdown);
        var newerEvidence = files.Where(path =>
                !WorkspaceFilePolicy.IsGeneratedArtifact(path) && File.GetLastWriteTimeUtc(path) > generatedAt)
            .Take(10).ToArray();
        if (newerEvidence.Length == 0) return;
        findings.Add(new("STALE_MASTER_BOOK", ComplianceSeverity.Critical,
            "O Livro Mestre é anterior a documentos ou partes atualmente existentes.",
            "Regenerar o Livro Mestre somente após concluir duplicidade, organização e divisão.",
            newerEvidence.Select(path => Relative(root, path)).ToArray()));
    }

    private static void DetectRootNaming(string root, ICollection<ComplianceFinding> findings)
    {
        var name = Path.GetFileName(root);
        if (!name.Contains("teste", StringComparison.OrdinalIgnoreCase) && !name.Contains("....", StringComparison.Ordinal))
            return;
        findings.Add(new("PROVISIONAL_ROOT_NAME", ComplianceSeverity.Warning,
            "O nome da pasta raiz ainda contém marcador provisório de teste.",
            "Renomear manualmente fora do Organiza, pois o aplicativo protege o nome da raiz por projeto.", [name]));
    }

    private async Task<IReadOnlyList<DuplicateAuditGroup>> FindDuplicateGroupsAsync(
        IReadOnlyList<string> files,
        CancellationToken cancellationToken)
    {
        var result = new List<DuplicateAuditGroup>();
        foreach (var sizeGroup in files.GroupBy(fileSystem.GetFileLength).Where(group => group.Count() > 1))
        {
            var hashes = new List<(string Path, string Hash)>();
            foreach (var path in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hashes.Add((path, await hashCalculator.ComputeSha256Async(path, cancellationToken)));
            }
            result.AddRange(hashes.GroupBy(item => item.Hash, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => new DuplicateAuditGroup(group.Key, group.Select(item => item.Path).ToArray())));
        }
        return result;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path);

    private sealed record DuplicateAuditGroup(string Hash, IReadOnlyList<string> Paths)
    {
        public int Count => Paths.Count;
    }

    [GeneratedRegex(@"\b(?:V\d{3}|P\d{3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DossierFolderRegex();

    [GeneratedRegex(@"_parte-\d+(?:_\d+)?\.pdf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedPartRegex();

    [GeneratedRegex(@"_parte-(\d+)(?:_\d+)?\.pdf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedPartNumberRegex();
}
