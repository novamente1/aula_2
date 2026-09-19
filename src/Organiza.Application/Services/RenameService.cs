using System.Text.RegularExpressions;
using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Domain.Files;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;
using Organiza.Domain.Selection;

namespace Organiza.Application.Services;

public sealed partial class RenameService(
    IFileSystem fileSystem,
    IContentSuggestionGateway suggestionGateway,
    DocumentAnalysisService documentAnalysis,
    PathGuard pathGuard,
    IHistoryStore history,
    EmptyFolderCleaner cleaner)
{
    public async Task<IReadOnlyList<RenameSuggestion>> GenerateNamesAsync(
        IReadOnlyList<FileItem> files,
        bool explicitGenerationRequest,
        CancellationToken cancellationToken = default)
    {
        if (!explicitGenerationRequest)
            throw new ApprovalRequiredException("Gerar nomes para todos");

        var documents = await documentAnalysis.AnalyzeAsync(files, cancellationToken);
        return await GenerateNamesFromAnalysesAsync(documents, explicitGenerationRequest, cancellationToken);
    }

    public async Task<IReadOnlyList<RenameSuggestion>> GenerateNamesFromAnalysesAsync(
        IReadOnlyList<DocumentAnalysis> documents,
        bool explicitGenerationRequest,
        CancellationToken cancellationToken = default)
    {
        if (!explicitGenerationRequest)
            throw new ApprovalRequiredException("Gerar nomes para todos");

        var suggestions = await suggestionGateway.GenerateAsync(documents, cancellationToken);
        var canonicalSuggestions = suggestions.Select(EnsureCanonicalDestination).ToArray();
        var distinctSuggestions = EnsureDifferentContentHasDifferentNames(
            canonicalSuggestions.Select(item => item with { IsSelected = true }).ToArray(), documents);
        var namedSuggestions = OfficialFileNamingProtocol.Apply(distinctSuggestions, documents);
        var contextChecked = BatchContextIsolationService.Apply(namedSuggestions, documents);
        return OfficialFileNameQualityGate.Apply(contextChecked);
    }

    public async Task<RenameBatchResult> ApplySelectedAsync(
        IReadOnlyList<RenameSuggestion> suggestions,
        ExplicitApproval approval,
        string protectedRootPath,
        FolderOrganizationMode organizationMode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!approval.Granted) throw new ApprovalRequiredException("renomear arquivos selecionados");
        var selectedSuggestions = suggestions.Where(item => item.IsSelected).ToArray();
        var entries = new List<OperationLogEntry>();
        var lockedFiles = new List<LockedFileIssue>();
        var mappingEntries = new List<MappingIndexEntry>();
        var consolidationFailures = 0;
        var rootProtection = new RootProtectionGuard();
        var folderManager = new StandardFolderManager(fileSystem);
        if (organizationMode == FolderOrganizationMode.ApplyStandardStructure)
            folderManager.CreateAllMissing(protectedRootPath, approval);
        var lockInspector = new FileLockInspector();
        var preflightLocks = selectedSuggestions
            .Select(item => lockInspector.CheckWriteAccess(item.OriginalPath))
            .Where(item => item is not null)
            .Cast<LockedFileIssue>()
            .ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        for (var itemIndex = 0; itemIndex < selectedSuggestions.Length; itemIndex++)
        {
            var suggestion = selectedSuggestions[itemIndex];
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Aplicando alteração física {itemIndex + 1} de {selectedSuggestions.Length}: {Path.GetFileName(suggestion.OriginalPath)}");
            try
            {
                if (preflightLocks.TryGetValue(Path.GetFullPath(suggestion.OriginalPath), out var preflightLock))
                {
                    lockedFiles.Add(preflightLock);
                    entries.Add(new(DateTimeOffset.Now, "Pré-validar bloqueio de arquivo", preflightLock.Path, null,
                        OperationStatus.Failed, preflightLock.Message));
                    continue;
                }
                if (!fileSystem.FileExists(suggestion.OriginalPath))
                {
                    entries.Add(new(DateTimeOffset.Now, "Renomear arquivo", suggestion.OriginalPath, null,
                        OperationStatus.Failed, "Arquivo de origem não encontrado; lote continuou."));
                    continue;
                }
                var safeName = EnforceNamingRules(suggestion.SuggestedName, Path.GetFileName(suggestion.OriginalPath));
                var destinationDirectory = Path.GetDirectoryName(suggestion.OriginalPath)!;
                if (organizationMode == FolderOrganizationMode.ApplyStandardStructure &&
                    !string.IsNullOrWhiteSpace(suggestion.DestinationFolder))
                {
                    var allowedDestinations = StandardFolders.All
                        .Append(StandardFolders.ContextReview)
                        .Append(StandardFolders.QualityReview);
                    var canonicalFolder = allowedDestinations.FirstOrDefault(folder =>
                        StandardFolderManager.Normalize(folder) ==
                        StandardFolderManager.Normalize(suggestion.DestinationFolder));
                    if (canonicalFolder is null)
                        throw new InvalidOperationException($"Pasta sugerida fora da estrutura padrão: {suggestion.DestinationFolder}.");
                    destinationDirectory = folderManager.ResolveOrCreate(
                        protectedRootPath, canonicalFolder, approval);
                }
                safeName = PathNameShortener.FitFileName(destinationDirectory, safeName);
                var desired = Path.Combine(destinationDirectory, safeName);
                rootProtection.EnsureInternalFileOperation(protectedRootPath, suggestion.OriginalPath, desired);
                if (fileSystem.DirectoryExists(suggestion.OriginalPath))
                    throw new InvalidOperationException("Pastas não podem ser renomeadas pelo fluxo de arquivos.");
                var sourceFullPath = Path.GetFullPath(suggestion.OriginalPath);
                var desiredFullPath = Path.GetFullPath(desired);
                if (string.Equals(sourceFullPath, desiredFullPath, StringComparison.Ordinal))
                {
                    entries.Add(new(DateTimeOffset.Now, "Renomear arquivo", suggestion.OriginalPath, desired,
                        OperationStatus.Skipped, "Nome e pasta já estavam adequados; nenhuma alteração física era necessária."));
                    mappingEntries.Add(new(suggestion.PrincipalId, suggestion.DocumentType ?? "Não determinado", desiredFullPath));
                    continue;
                }
                var immediateLock = lockInspector.CheckWriteAccess(suggestion.OriginalPath);
                if (immediateLock is not null)
                {
                    lockedFiles.Add(immediateLock);
                    entries.Add(new(DateTimeOffset.Now, "Pré-validar bloqueio de arquivo", immediateLock.Path, null,
                        OperationStatus.Failed, immediateLock.Message));
                    continue;
                }
                if (string.Equals(sourceFullPath, desiredFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    await RenameCaseOnlyAsync(protectedRootPath, sourceFullPath, desiredFullPath,
                        rootProtection, cancellationToken);
                    entries.Add(new(DateTimeOffset.Now, "Renomear arquivo", suggestion.OriginalPath, desiredFullPath,
                        OperationStatus.Completed, "Alteração de maiúsculas/minúsculas aplicada fisicamente."));
                    mappingEntries.Add(new(suggestion.PrincipalId, suggestion.DocumentType ?? "Não determinado", desiredFullPath));
                    continue;
                }
                var destination = GetAvailableDestination(desired);
                rootProtection.EnsureInternalFileOperation(protectedRootPath, suggestion.OriginalPath, destination);
                pathGuard.EnsureAllowed(destination);
                await MoveWithRetryAsync(suggestion.OriginalPath, destination, cancellationToken);
                if (fileSystem.FileExists(suggestion.OriginalPath) || !fileSystem.FileExists(destination))
                    throw new IOException("A alteração física não pôde ser confirmada no Google Drive.");
                var conflictResolved = !string.Equals(desired, destination, StringComparison.OrdinalIgnoreCase);
                var movedCategory = !string.Equals(Path.GetDirectoryName(sourceFullPath),
                    Path.GetDirectoryName(destination), StringComparison.OrdinalIgnoreCase);
                entries.Add(new(DateTimeOffset.Now, "Renomear arquivo", suggestion.OriginalPath, destination,
                    OperationStatus.Completed, BuildCompletionDetails(conflictResolved, movedCategory,
                        Path.GetFileName(Path.GetDirectoryName(destination)))));
                mappingEntries.Add(new(suggestion.PrincipalId, suggestion.DocumentType ?? "Não determinado", destination));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                entries.Add(new(DateTimeOffset.Now, "Renomear arquivo", suggestion.OriginalPath, null,
                    OperationStatus.Failed,
                    $"{exception.GetType().Name}: {exception.Message}. O lote continuou."));
            }
        }

        if (organizationMode == FolderOrganizationMode.ApplyStandardStructure)
        {
            progress?.Report("Consolidando pastas padrão equivalentes e preservando todos os arquivos...");
            var consolidation = folderManager.ConsolidateSimilar(protectedRootPath, approval);
            entries.AddRange(consolidation.MovedFiles.Select(move => new OperationLogEntry(
                DateTimeOffset.Now, "Consolidar pasta padrão", move.SourcePath, move.DestinationPath,
                OperationStatus.Completed, "Conteúdo movido para a categoria canônica sem sobrescrita.")));
            entries.AddRange(consolidation.RemovedFolders.Select(path => new OperationLogEntry(
                DateTimeOffset.Now, "Remover pasta vazia", path, null, OperationStatus.Completed,
                "Pasta equivalente esvaziada após consolidação.")));
            entries.AddRange(consolidation.Failures.Select(failure => new OperationLogEntry(
                DateTimeOffset.Now, "Consolidar pasta padrão", failure.SourcePath, null,
                OperationStatus.Failed, $"{failure.Details}. O restante do lote continuou.")));
            consolidationFailures = consolidation.Failures.Count;
        }

        entries.AddRange(cleaner.RemoveEmptyFolders(protectedRootPath).Select(path => new OperationLogEntry(
            DateTimeOffset.Now, "Remover pasta vazia", path, null, OperationStatus.Completed)));
        string? mappingIndexPath = null;
        if (mappingEntries.Count > 0)
        {
            mappingIndexPath = new MappingIndexService(fileSystem).Append(protectedRootPath, mappingEntries);
            entries.Add(new(DateTimeOffset.Now, "Atualizar índice de mapeamento", protectedRootPath, mappingIndexPath,
                OperationStatus.Completed,
                $"{mappingEntries.Count} documento(s) registrado(s); identificadores ausentes permaneceram vazios."));
        }
        await history.AppendAsync(entries, cancellationToken);
        var renameEntries = entries.Where(entry => entry.Operation == "Renomear arquivo").ToArray();
        return new(selectedSuggestions.Length,
            renameEntries.Count(entry => entry.Status == OperationStatus.Completed),
            renameEntries.Count(entry => entry.Status == OperationStatus.Skipped),
            renameEntries.Count(entry => entry.Status == OperationStatus.Failed) + lockedFiles.Count + consolidationFailures,
            lockedFiles.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            mappingIndexPath);
    }

    private static string? BuildCompletionDetails(bool conflictResolved, bool movedCategory, string? category)
    {
        var details = new List<string>();
        if (movedCategory) details.Add($"Arquivo classificado e movido para {category}.");
        if (conflictResolved) details.Add("Conflito de nome resolvido com sufixo numérico incremental.");
        return details.Count == 0 ? null : string.Join(' ', details);
    }

    private async Task RenameCaseOnlyAsync(string protectedRootPath, string source, string destination,
        RootProtectionGuard rootProtection, CancellationToken cancellationToken)
    {
        var internalDirectory = Path.Combine(protectedRootPath, WorkspaceFilePolicy.InternalDirectoryName);
        fileSystem.CreateDirectory(internalDirectory);
        var temporary = Path.Combine(internalDirectory, $"rename-{Guid.NewGuid():N}.tmp");
        rootProtection.EnsureInternalFileOperation(protectedRootPath, source, temporary);
        rootProtection.EnsureInternalFileOperation(protectedRootPath, temporary, destination);
        pathGuard.EnsureAllowed(temporary);
        pathGuard.EnsureAllowed(destination);
        await MoveWithRetryAsync(source, temporary, cancellationToken);
        try
        {
            await MoveWithRetryAsync(temporary, destination, cancellationToken);
        }
        catch
        {
            if (fileSystem.FileExists(temporary) && !fileSystem.FileExists(source))
            {
                try { await MoveWithRetryAsync(temporary, source, CancellationToken.None); }
                catch { }
            }
            throw;
        }
    }

    private async Task MoveWithRetryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                fileSystem.MoveFile(source, destination);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (!fileSystem.FileExists(source) && fileSystem.FileExists(destination)) return;
                lastError = exception;
                if (attempt < 5)
                    await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }

        throw new IOException($"O Google Drive não liberou a renomeação após 5 tentativas: {Path.GetFileName(source)}.",
            lastError);
    }

    private string GetAvailableDestination(string desiredPath)
    {
        if (!fileSystem.FileExists(desiredPath)) return desiredPath;
        var directory = Path.GetDirectoryName(desiredPath)!;
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);
        for (var index = 2; ; index++)
        {
            var candidateName = PathNameShortener.FitFileName(directory, $"{stem}_{index}{extension}");
            var candidate = Path.Combine(directory, candidateName);
            if (!fileSystem.FileExists(candidate)) return candidate;
        }
    }

    public static string EnforceNamingRules(string suggestedName, string originalName)
    {
        var originalExtension = Path.GetExtension(originalName);
        var stem = Path.GetFileNameWithoutExtension(suggestedName).Trim();
        stem = LegacyCopyPrefixRegex().Replace(stem, string.Empty);
        stem = LegacyCopySuffixRegex().Replace(stem, string.Empty);
        stem = DuplicateNumberSuffixRegex().Replace(stem, string.Empty);
        if (ArtificialFieldRegex().IsMatch(stem))
            throw new ArgumentException("O nome contém campo vazio ou tag artificial e deve ser editado manualmente.", nameof(suggestedName));
        stem = RepeatedYearRegex().Replace(stem, "$1");
        stem = WhitespaceRegex().Replace(stem, " ").Trim(' ', '-', '_');
        if (stem.Length == 0) throw new ArgumentException("O nome sugerido não pode ficar vazio.", nameof(suggestedName));

        var sentence = stem.ToLowerInvariant();
        sentence = char.ToUpperInvariant(sentence[0]) + sentence[1..];

        var identifiersToPreserve = ProcessIdentifierRegex().Matches(Path.GetFileNameWithoutExtension(originalName)).Cast<Match>()
            .Concat(ProcessIdentifierRegex().Matches(stem).Cast<Match>())
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var identifier in identifiersToPreserve)
        {
            var position = sentence.IndexOf(identifier, StringComparison.OrdinalIgnoreCase);
            sentence = position >= 0
                ? sentence.Remove(position, identifier.Length).Insert(position, identifier)
                : $"{identifier} {sentence}";
        }

        foreach (Match plate in VehiclePlateRegex().Matches(stem))
        {
            var position = sentence.IndexOf(plate.Value, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
                sentence = sentence.Remove(position, plate.Length).Insert(position, plate.Value.ToUpperInvariant());
        }

        return sentence + originalExtension;
    }

    private static RenameSuggestion EnsureCanonicalDestination(RenameSuggestion suggestion)
    {
        var allowed = StandardFolders.Numbered
            .Append(StandardFolders.ContextReview)
            .Append(StandardFolders.QualityReview);
        var canonical = allowed.FirstOrDefault(folder =>
            StandardFolderManager.Normalize(folder) ==
            StandardFolderManager.Normalize(suggestion.DestinationFolder ?? string.Empty));
        if (canonical is not null) return suggestion with { DestinationFolder = canonical };

        return suggestion with
        {
            DestinationFolder = StandardFolders.Research,
            Reason = $"{suggestion.Reason} Pasta não padronizada corrigida para {StandardFolders.Research}.".Trim()
        };
    }

    [GeneratedRegex(@"\b((?:19|20)\d{2})(?:[\s_-]+\1)+\b", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedYearRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:(?:c[oó]pia|copy)(?:\s+de|\s+of)?)[\s_-]+", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyCopyPrefixRegex();

    [GeneratedRegex(@"[\s_-]+(?:c[oó]pia|copy)(?:\s+\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyCopySuffixRegex();

    [GeneratedRegex(@"\s*\(\d+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateNumberSuffixRegex();

    [GeneratedRegex(@"\b[A-Za-z]{2,12}_\d{6,}(?:[-.]\d+)*\b")]
    private static partial Regex ProcessIdentifierRegex();

    [GeneratedRegex(@"\b[A-Z]{3}[0-9][A-Z0-9][0-9]{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex VehiclePlateRegex();

    [GeneratedRegex(@"(?:\[(?:tag|tipo|categoria|ano)?[^\]]*\]|\{[^}]*\}|\b(?:n/?a|null|undefined|sem (?:data|tipo|nome))\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ArtificialFieldRegex();

    private static IReadOnlyList<RenameSuggestion> EnsureDifferentContentHasDifferentNames(
        IReadOnlyList<RenameSuggestion> suggestions,
        IReadOnlyList<DocumentAnalysis> documents)
    {
        var byPath = documents.ToDictionary(document => document.File.FullPath, StringComparer.OrdinalIgnoreCase);
        var result = suggestions.ToArray();
        foreach (var group in result.GroupBy(item => item.SuggestedName, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.Where(item => byPath.ContainsKey(item.OriginalPath)).ToArray();
            if (members.Select(item => byPath[item.OriginalPath].ContentSha256)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1) continue;

            foreach (var member in members)
            {
                var index = Array.IndexOf(result, member);
                var document = byPath[member.OriginalPath];
                var extension = Path.GetExtension(member.SuggestedName);
                var stem = Path.GetFileNameWithoutExtension(member.SuggestedName);
                var discriminator = GetNaturalDiscriminator(document);
                result[index] = member with
                {
                    SuggestedName = $"{stem} - {discriminator}{extension}",
                    Reason = $"{member.Reason} Conteúdo distinto identificado por {discriminator}.".Trim()
                };
            }
        }

        foreach (var group in result.GroupBy(item => item.SuggestedName, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Select(item => byPath.GetValueOrDefault(item.OriginalPath)?.ContentSha256)
                         .Where(hash => hash is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            foreach (var member in group)
            {
                var index = Array.IndexOf(result, member);
                var document = byPath[member.OriginalPath];
                var extension = Path.GetExtension(member.SuggestedName);
                var stem = Path.GetFileNameWithoutExtension(member.SuggestedName);
                var shortHash = document.ContentSha256[..Math.Min(8, document.ContentSha256.Length)].ToUpperInvariant();
                result[index] = member with
                {
                    SuggestedName = $"{stem} - ref {shortHash}{extension}",
                    Reason = $"{member.Reason} Referência técnica acrescentada para distinguir conteúdo diferente.".Trim()
                };
            }
        }

        return result;
    }

    private static string GetNaturalDiscriminator(DocumentAnalysis document)
    {
        if (document.ProcessIdentifiers.Count > 0)
        {
            var process = document.ProcessIdentifiers[0];
            var shortProcess = Regex.Match(process, @"\d{7}-\d{2}").Value;
            return string.IsNullOrWhiteSpace(shortProcess) ? process : $"proc {shortProcess}";
        }
        if (document.DocumentDates.Count > 0) return document.DocumentDates[0].Replace('/', '-').Replace('.', '-');
        var originalStem = Path.GetFileNameWithoutExtension(document.File.Name).Trim();
        var contentWords = ContentWordRegex().Matches(document.FirstPageText).Cast<Match>()
            .Select(match => match.Value).Take(6).ToArray();
        var naturalText = contentWords.Length == 0 ? originalStem : string.Join(' ', contentWords);
        return $"{naturalText} {document.File.SizeBytes} bytes";
    }

    [GeneratedRegex(@"[\p{L}\p{N}._-]+")]
    private static partial Regex ContentWordRegex();
}
