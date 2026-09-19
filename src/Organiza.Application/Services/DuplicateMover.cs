using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed class DuplicateMover(
    IFileSystem fileSystem,
    StandardFolderManager folders,
    PathGuard pathGuard,
    IHistoryStore history,
    EmptyFolderCleaner cleaner)
{
    public DuplicateDestinationDecision ResolveDestination(string rootPath) =>
        new DuplicateDestinationResolver(fileSystem).Resolve(rootPath);

    public async Task MoveConfirmedAsync(
        string rootPath,
        IReadOnlyList<string> confirmedCopies,
        ExplicitApproval approval,
        CancellationToken cancellationToken = default)
    {
        if (!approval.Granted)
        {
            throw new ApprovalRequiredException("mover cópias para a pasta de duplicados aplicável ao contexto");
        }

        if (confirmedCopies.Any(path => !WorkspaceFilePolicy.IsInteractiveCandidate(rootPath, path)))
            throw new InvalidOperationException(
                "Uma das cópias está fora da raiz protegida ou em uma área reservada do Organiza.");

        var rootProtection = new RootProtectionGuard();
        var decision = ResolveDestination(rootPath);
        var destinationFolder = folders.ResolveOrCreate(rootPath, decision.FolderName, approval);
        var entries = new List<OperationLogEntry>();
        foreach (var source in confirmedCopies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var destination = GetUniqueDestination(destinationFolder, Path.GetFileName(source));
                rootProtection.EnsureInternalFileOperation(rootPath, source, destination);
                pathGuard.EnsureAllowed(destination);
                fileSystem.MoveFile(source, destination);
                entries.Add(new(DateTimeOffset.Now, "Mover duplicado", source, destination, OperationStatus.Completed,
                    $"{decision.RuleCode}: {decision.Explanation}"));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                entries.Add(new(DateTimeOffset.Now, "Mover duplicado", source, null, OperationStatus.Failed,
                    $"{exception.GetType().Name}: {exception.Message}. As demais cópias continuaram."));
            }
        }

        var removedFolders = cleaner.RemoveEmptyFolders(rootPath);
        entries.AddRange(removedFolders.Select(path => new OperationLogEntry(
            DateTimeOffset.Now, "Remover pasta vazia", path, null, OperationStatus.Completed)));
        await history.AppendAsync(entries, cancellationToken);
    }

    private string GetUniqueDestination(string directory, string fileName)
    {
        fileName = PathNameShortener.FitFileName(directory, fileName);
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
}
