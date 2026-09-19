using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Domain.Files;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;
using System.Text.RegularExpressions;

namespace Organiza.Application.Services;

public sealed class PdfSplitService(
    IFileSystem fileSystem,
    IHashCalculator hashCalculator,
    IPdfDocumentAdapter pdf,
    StandardFolderManager folders,
    PathGuard pathGuard,
    IHistoryStore history,
    EmptyFolderCleaner cleaner)
{
    public async Task<PdfSplitResult> SplitAsync(
        string sourcePath,
        string workingRoot,
        ExplicitApproval approval,
        long maximumPartBytes = PdfSafetyLimits.PartSizeBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SplitCoreAsync(sourcePath, workingRoot, approval, maximumPartBytes, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await TryLogFailureAsync(sourcePath, exception);
            throw new PdfSplitOperationException(sourcePath,
                $"A divisão de '{Path.GetFileName(sourcePath)}' foi interrompida. O original foi preservado e as partes desta tentativa foram submetidas à limpeza automática.",
                exception);
        }
        catch (ApprovalRequiredException)
        {
            throw;
        }
        catch (UnsafePathException)
        {
            throw;
        }
        catch (PdfSplitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await TryLogFailureAsync(sourcePath, exception);
            throw new PdfSplitOperationException(sourcePath, BuildFriendlyMessage(sourcePath, exception), exception);
        }
    }

    private async Task<PdfSplitResult> SplitCoreAsync(
        string sourcePath,
        string workingRoot,
        ExplicitApproval approval,
        long maximumPartBytes = PdfSafetyLimits.PartSizeBytes,
        CancellationToken cancellationToken = default)
    {
        if (!approval.Granted)
        {
            throw new ApprovalRequiredException("dividir PDF e preservar original");
        }

        if (!fileSystem.FileExists(sourcePath)) throw new FileNotFoundException("PDF não encontrado.", sourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Somente arquivos PDF podem ser divididos.", nameof(sourcePath));
        if (maximumPartBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPartBytes));

        var rootProtection = new RootProtectionGuard();
        if (!WorkspaceFilePolicy.IsPdfSplitCandidate(workingRoot, sourcePath))
            throw new InvalidOperationException(
                "O PDF está fora da raiz, em uma pasta de preservação ou já é uma parte gerada pelo Organiza.");

        var staleParts = FindGeneratedPartsForSource(sourcePath);
        if (staleParts.Count > 0)
        {
            var notRemoved = await DeleteWithRetryAsync(staleParts, CancellationToken.None);
            if (notRemoved.Count > 0)
                throw new IOException($"Existem {notRemoved.Count} parte(s) residual(is) bloqueada(s) pelo Drive. Aguarde a sincronização e tente novamente: {string.Join(", ", notRemoved.Select(Path.GetFileName))}");
            await history.AppendAsync(staleParts.Select(path => new OperationLogEntry(
                DateTimeOffset.Now, "Limpar parte residual de PDF", path, null, OperationStatus.Completed,
                "Parte de tentativa anterior removida antes do reinício seguro da divisão")), cancellationToken);
        }

        var sourceSize = fileSystem.GetFileLength(sourcePath);
        var protectedMode = sourceSize > PdfSafetyLimits.ProtectedModeThresholdBytes;
        var scratchDirectory = Path.Combine(Path.GetTempPath(), "Organiza", "PdfSplit");
        fileSystem.CreateDirectory(scratchDirectory);

        var originalFolder = folders.ResolveOrCreate(workingRoot, StandardFolders.Originals, approval);
        var preservedName = PathNameShortener.FitFileName(originalFolder, Path.GetFileName(sourcePath));
        var preferredPreservedPath = Path.Combine(originalFolder, preservedName);
        var reusePreservedOriginal = fileSystem.FileExists(preferredPreservedPath) &&
                                     await AreIdenticalAsync(sourcePath, preferredPreservedPath, cancellationToken);
        var preservedPath = reusePreservedOriginal
            ? preferredPreservedPath
            : GetUniquePath(originalFolder, preservedName);
        rootProtection.EnsureInternalFileOperation(workingRoot, sourcePath, preservedPath);
        pathGuard.EnsureAllowed(preservedPath);

        if (!reusePreservedOriginal)
        {
            fileSystem.CopyFile(sourcePath, preservedPath);
            await EnsureIdenticalAsync(sourcePath, preservedPath, cancellationToken);
        }

        var pageCount = pdf.GetPageCount(sourcePath);
        var parts = new List<PdfPart>();
        var nextPage = 0;
        var partNumber = 1;
        var sourceRemoved = false;

        try
        {
            while (nextPage < pageCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var partDirectory = Path.GetDirectoryName(sourcePath)!;
                var partName = PathNameShortener.FitFileName(partDirectory,
                    $"{CleanStem(Path.GetFileNameWithoutExtension(sourcePath))}_parte-{partNumber:00}.pdf");
                var partPath = Path.Combine(partDirectory, partName);
                rootProtection.EnsureInternalFileOperation(workingRoot, sourcePath, partPath);
                pathGuard.EnsureAllowed(partPath);
                if (fileSystem.FileExists(partPath))
                    throw new IOException($"O destino já existe e não será sobrescrito: {partPath}");
                var candidate = FindLargestPart(
                    sourcePath, nextPage, pageCount, maximumPartBytes, scratchDirectory, cancellationToken);

                try
                {
                    // File.Copy/CopyFile transfere em fluxo pelo sistema operacional. Nenhuma parte
                    // de até 97 MiB é materializada como byte[] na memória do aplicativo.
                    fileSystem.CopyFile(candidate.FullPath, partPath);
                    var actualSize = fileSystem.GetFileLength(partPath);
                    var isOversizedSinglePage = candidate.PageCount == 1 && candidate.SizeBytes > maximumPartBytes;
                    if (actualSize != candidate.SizeBytes || (actualSize > maximumPartBytes && !isOversizedSinglePage))
                    {
                        fileSystem.DeleteFile(partPath);
                        throw new IOException($"A parte gerada possui {actualSize} bytes e viola o limite real de {maximumPartBytes} bytes.");
                    }
                    parts.Add(new(partPath, partNumber, actualSize, nextPage + 1,
                        nextPage + candidate.PageCount, candidate.Rasterized));
                    nextPage += candidate.PageCount;
                    partNumber++;
                }
                catch
                {
                    if (fileSystem.FileExists(partPath))
                    {
                        try { fileSystem.DeleteFile(partPath); } catch { }
                    }
                    throw;
                }
                finally
                {
                    TryDeleteScratchFile(candidate.FullPath);
                }
            }

            ValidateCompleteCoverage(parts, pageCount);

            fileSystem.DeleteFile(sourcePath);
            sourceRemoved = true;
            var entries = new List<OperationLogEntry>
            {
                new(DateTimeOffset.Now, "Preservar PDF original", sourcePath, preservedPath,
                    OperationStatus.Completed, reusePreservedOriginal
                        ? "Original idêntico já estava preservado; reutilizado após validação por tamanho e SHA-256"
                        : "Cópia validada por tamanho e SHA-256")
            };
            entries.AddRange(parts.Select(part => new OperationLogEntry(
                DateTimeOffset.Now, "Criar parte de PDF por tamanho real", sourcePath, part.FullPath,
                OperationStatus.Completed,
                $"Páginas {part.FirstPage}-{part.LastPage}; {part.SizeBytes} bytes; limite {maximumPartBytes} bytes; " +
                $"modo protegido em disco: {(protectedMode ? "ativo" : "normal")}; " +
                $"rasterização de segurança: {(part.Rasterized ? "aplicada" : "não necessária")}")));
            entries.AddRange(cleaner.RemoveEmptyFolders(workingRoot).Select(path => new OperationLogEntry(
                DateTimeOffset.Now, "Remover pasta vazia", path, null, OperationStatus.Completed)));
            await history.AppendAsync(entries, cancellationToken);
            return new PdfSplitResult(sourcePath, preservedPath, parts);
        }
        catch (Exception originalException)
        {
            var generatedInThisAttempt = parts
                .Where(part => !sourceRemoved && fileSystem.FileExists(part.FullPath))
                .Select(part => part.FullPath)
                .ToArray();
            var notRemoved = await DeleteWithRetryAsync(generatedInThisAttempt, CancellationToken.None);
            if (notRemoved.Count > 0)
            {
                throw new IOException(
                    $"A divisão falhou e {notRemoved.Count} parte(s) residual(is) permaneceram bloqueadas pelo Drive: {string.Join(", ", notRemoved.Select(Path.GetFileName))}",
                    originalException);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalException).Throw();
            throw;
        }
    }

    private IReadOnlyList<string> FindGeneratedPartsForSource(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath)!;
        var expectedFirstName = PathNameShortener.FitFileName(directory,
            $"{CleanStem(Path.GetFileNameWithoutExtension(sourcePath))}_parte-01.pdf");
        var prefix = Regex.Replace(Path.GetFileNameWithoutExtension(expectedFirstName), @"\d{2}$", string.Empty);
        return fileSystem.EnumerateFiles(directory, SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                           WorkspaceFilePolicy.IsGeneratedPdfPart(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> DeleteWithRetryAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var pending = paths.Where(fileSystem.FileExists).ToList();
        for (var attempt = 1; attempt <= 5 && pending.Count > 0; attempt++)
        {
            foreach (var path in pending.ToArray())
            {
                try
                {
                    fileSystem.DeleteFile(path);
                    if (!fileSystem.FileExists(path)) pending.Remove(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            if (pending.Count > 0 && attempt < 5)
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }

        return pending;
    }

    private async Task TryLogFailureAsync(string sourcePath, Exception exception)
    {
        try
        {
            await history.AppendAsync(
            [
                new(DateTimeOffset.Now, "Falha ao dividir PDF", sourcePath, null, OperationStatus.Failed,
                    $"{exception.GetType().Name}: {exception.Message}")
            ], CancellationToken.None);
        }
        catch
        {
            // O log não pode provocar o encerramento do aplicativo quando o Drive estiver indisponível.
        }
    }

    private static string BuildFriendlyMessage(string sourcePath, Exception exception)
    {
        var fileName = Path.GetFileName(sourcePath);
        return exception switch
        {
            FileNotFoundException => $"O PDF '{fileName}' não foi encontrado. Ele pode ter sido movido durante a sincronização do Google Drive.",
            UnauthorizedAccessException => $"Sem permissão para acessar '{fileName}'. Verifique as permissões da pasta e o status do Google Drive. Detalhe: {exception.Message}",
            IOException => $"Não foi possível ler ou gravar '{fileName}'. O arquivo pode estar bloqueado ou sincronizando pelo Google Drive. Detalhe: {exception.Message}",
            InvalidOperationException => $"O PDF '{fileName}' não pôde ser processado. Ele pode estar corrompido, protegido ou possuir uma estrutura incompatível. Detalhe: {exception.Message}",
            OutOfMemoryException => $"O PDF '{fileName}' excedeu a capacidade segura do processador de PDF. O original foi preservado e nenhum arquivo incompleto será mantido.",
            _ => $"Falha inesperada ao processar '{fileName}'. Detalhe técnico: {exception.Message}"
        };
    }

    private ScratchPdfPart FindLargestPart(
        string sourcePath,
        int firstPage,
        int totalPages,
        long limit,
        string scratchDirectory,
        CancellationToken cancellationToken)
    {
        var low = 1;
        var high = totalPages - firstPage;
        if (high <= 128)
            return FindLargestPartDescending(sourcePath, firstPage, high, limit, scratchDirectory,
                cancellationToken);
        var bestCount = 0;
        string? bestPath = null;
        long bestSize = 0;
        string? oversizedSinglePagePath = null;
        long oversizedSinglePageSize = 0;

        try
        {
            while (low <= high)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidateCount = low + ((high - low) / 2);
                var candidatePath = CreateScratchPath(scratchDirectory);
                try
                {
                    pdf.RenderPagesToFile(sourcePath, firstPage, candidateCount, candidatePath);
                }
                catch
                {
                    TryDeleteScratchFile(candidatePath);
                    throw;
                }
                var candidateSize = fileSystem.GetFileLength(candidatePath);
                if (candidateSize <= limit)
                {
                    TryDeleteScratchFile(bestPath);
                    bestCount = candidateCount;
                    bestPath = candidatePath;
                    bestSize = candidateSize;
                    low = candidateCount + 1;
                }
                else
                {
                    if (candidateCount == 1)
                    {
                        TryDeleteScratchFile(oversizedSinglePagePath);
                        oversizedSinglePagePath = candidatePath;
                        oversizedSinglePageSize = candidateSize;
                    }
                    else
                    {
                        TryDeleteScratchFile(candidatePath);
                    }
                    high = candidateCount - 1;
                }
            }

            if (bestPath is not null)
            {
                TryDeleteScratchFile(oversizedSinglePagePath);
                return new(bestCount, bestPath, bestSize, false);
            }

            // Uma página PDF é a menor unidade estrutural válida. Quando ela sozinha
            // excede o teto, precisa ser preservada inteira em uma parte excepcional.
            if (oversizedSinglePagePath is not null)
            {
                var compressedPath = CreateScratchPath(scratchDirectory);
                try
                {
                    if (pdf.TryRenderSinglePageWithinLimit(
                            sourcePath, firstPage, limit, compressedPath))
                    {
                        var compressedSize = fileSystem.GetFileLength(compressedPath);
                        if (compressedSize > 0 && compressedSize <= limit)
                        {
                            TryDeleteScratchFile(oversizedSinglePagePath);
                            return new(1, compressedPath, compressedSize, true);
                        }
                    }
                    TryDeleteScratchFile(compressedPath);
                }
                catch
                {
                    TryDeleteScratchFile(compressedPath);
                }

                return new(1, oversizedSinglePagePath, oversizedSinglePageSize, false);
            }

            var fallbackPath = CreateScratchPath(scratchDirectory);
            try
            {
                pdf.RenderPagesToFile(sourcePath, firstPage, 1, fallbackPath);
            }
            catch
            {
                TryDeleteScratchFile(fallbackPath);
                throw;
            }
            return new(1, fallbackPath, fileSystem.GetFileLength(fallbackPath), false);
        }
        catch
        {
            TryDeleteScratchFile(bestPath);
            TryDeleteScratchFile(oversizedSinglePagePath);
            throw;
        }
    }

    private ScratchPdfPart FindLargestPartDescending(
        string sourcePath,
        int firstPage,
        int remainingPages,
        long limit,
        string scratchDirectory,
        CancellationToken cancellationToken)
    {
        string? oversizedSinglePagePath = null;
        long oversizedSinglePageSize = 0;
        try
        {
            for (var candidateCount = remainingPages; candidateCount >= 1; candidateCount--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidatePath = CreateScratchPath(scratchDirectory);
                try
                {
                    pdf.RenderPagesToFile(sourcePath, firstPage, candidateCount, candidatePath);
                    var candidateSize = fileSystem.GetFileLength(candidatePath);
                    if (candidateSize <= limit)
                        return new(candidateCount, candidatePath, candidateSize, false);
                    if (candidateCount == 1)
                    {
                        oversizedSinglePagePath = candidatePath;
                        oversizedSinglePageSize = candidateSize;
                    }
                    else
                    {
                        TryDeleteScratchFile(candidatePath);
                    }
                }
                catch
                {
                    TryDeleteScratchFile(candidatePath);
                    throw;
                }
            }

            if (oversizedSinglePagePath is null)
                throw new InvalidOperationException("Não foi possível materializar a próxima página do PDF.");
            var compressedPath = CreateScratchPath(scratchDirectory);
            try
            {
                if (pdf.TryRenderSinglePageWithinLimit(sourcePath, firstPage, limit, compressedPath))
                {
                    var compressedSize = fileSystem.GetFileLength(compressedPath);
                    if (compressedSize > 0 && compressedSize <= limit)
                    {
                        TryDeleteScratchFile(oversizedSinglePagePath);
                        return new(1, compressedPath, compressedSize, true);
                    }
                }
                TryDeleteScratchFile(compressedPath);
            }
            catch
            {
                TryDeleteScratchFile(compressedPath);
            }

            var result = new ScratchPdfPart(1, oversizedSinglePagePath, oversizedSinglePageSize, false);
            oversizedSinglePagePath = null;
            return result;
        }
        catch
        {
            TryDeleteScratchFile(oversizedSinglePagePath);
            throw;
        }
    }

    private static void ValidateCompleteCoverage(IReadOnlyList<PdfPart> parts, int pageCount)
    {
        if (parts.Count == 0) throw new IOException("A divisão não gerou partes.");
        var expectedFirstPage = 1;
        foreach (var part in parts)
        {
            if (part.FirstPage != expectedFirstPage || part.LastPage < part.FirstPage)
                throw new IOException($"Cobertura de páginas descontínua antes da parte {part.PartNumber:00}.");
            expectedFirstPage = part.LastPage + 1;
        }
        if (expectedFirstPage != pageCount + 1)
            throw new IOException($"Cobertura incompleta: esperado até a página {pageCount}, obtido até {expectedFirstPage - 1}.");
    }

    private static string CreateScratchPath(string scratchDirectory) =>
        Path.Combine(scratchDirectory, $"parte-{Guid.NewGuid():N}.pdf");

    private void TryDeleteScratchFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !fileSystem.FileExists(path)) return;
        try
        {
            fileSystem.DeleteFile(path);
        }
        catch
        {
            // Arquivos de trabalho nunca podem mascarar a falha original.
        }
    }

    private sealed record ScratchPdfPart(int PageCount, string FullPath, long SizeBytes, bool Rasterized);

    private async Task EnsureIdenticalAsync(string source, string copy, CancellationToken cancellationToken)
    {
        if (!await AreIdenticalAsync(source, copy, cancellationToken))
        {
            throw new IOException("A cópia de preservação do PDF não corresponde ao original.");
        }
    }

    private async Task<bool> AreIdenticalAsync(string first, string second, CancellationToken cancellationToken)
    {
        if (fileSystem.GetFileLength(first) != fileSystem.GetFileLength(second)) return false;
        return string.Equals(
            await hashCalculator.ComputeSha256Async(first, cancellationToken),
            await hashCalculator.ComputeSha256Async(second, cancellationToken),
            StringComparison.OrdinalIgnoreCase);
    }

    private string GetUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!fileSystem.FileExists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(directory,
                PathNameShortener.FitFileName(directory, $"{stem}_{index}{extension}"));
            if (!fileSystem.FileExists(candidate)) return candidate;
        }
    }

    private static string CleanStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Where(character => !invalid.Contains(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "documento" : cleaned;
    }
}
