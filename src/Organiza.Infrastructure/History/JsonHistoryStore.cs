using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Organiza.Application.Abstractions;
using Organiza.Application.Services;
using Organiza.Domain.Operations;

namespace Organiza.Infrastructure.History;

public sealed class JsonHistoryStore : IHistoryStore
{
    public const string FileName = ".organiza_log.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _path;
    private readonly string _legacyRootPath;
    private readonly string _legacyTemporaryPath;
    private readonly string _internalDirectory;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _gate;

    public JsonHistoryStore(string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        _internalDirectory = Path.Combine(fullRoot, WorkspaceFilePolicy.InternalDirectoryName);
        _path = Path.Combine(_internalDirectory, "historico.json");
        _legacyRootPath = Path.Combine(fullRoot, FileName);
        _legacyTemporaryPath = _legacyRootPath + ".tmp";
        _lockPath = Path.Combine(_internalDirectory, "history.lock");
        _gate = Gates.GetOrAdd(_path, _ => new SemaphoreSlim(1, 1));
    }

    public async Task AppendAsync(IEnumerable<OperationLogEntry> entries, CancellationToken cancellationToken = default)
    {
        var additions = entries.ToArray();
        if (additions.Length == 0) return;

        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
            throw new IOException("O histórico está ocupado há mais de 10 segundos. Tente novamente.");
        FileStream? processLock = null;
        try
        {
            EnsureInternalDirectory();
            processLock = await AcquireProcessLockAsync(cancellationToken);
            var current = (await ReadCoreAsync(cancellationToken)).ToList();
            current.AddRange(additions);
            var temporaryPath = Path.Combine(_internalDirectory, $"history-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    File.SetAttributes(temporaryPath, File.GetAttributes(temporaryPath) | FileAttributes.Hidden);
                    await JsonSerializer.SerializeAsync(stream, current, Options, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, _path, overwrite: true);
                File.SetAttributes(_path, File.GetAttributes(_path) | FileAttributes.Hidden);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }
        finally
        {
            if (processLock is not null)
            {
                await processLock.DisposeAsync();
                TryDeleteLockFile();
            }
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationLogEntry>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
            throw new IOException("O histórico está ocupado há mais de 10 segundos. Tente novamente.");
        try
        {
            EnsureInternalDirectory();
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<OperationLogEntry>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<List<OperationLogEntry>>(stream, Options, cancellationToken) ?? [];
    }

    private void EnsureInternalDirectory()
    {
        Directory.CreateDirectory(_internalDirectory);
        File.SetAttributes(_internalDirectory,
            File.GetAttributes(_internalDirectory) | FileAttributes.Hidden | FileAttributes.System);
        if (!File.Exists(_path) && File.Exists(_legacyRootPath))
        {
            try
            {
                File.Move(_legacyRootPath, _path);
                File.SetAttributes(_path, File.GetAttributes(_path) | FileAttributes.Hidden);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (!File.Exists(_legacyTemporaryPath)) return;

        var migratedPath = Path.Combine(_internalDirectory,
            $"legacy-history-{File.GetLastWriteTimeUtc(_legacyTemporaryPath):yyyyMMddHHmmss}.json.tmp");
        try
        {
            if (File.Exists(migratedPath)) File.Delete(_legacyTemporaryPath);
            else File.Move(_legacyTemporaryPath, migratedPath);
            if (File.Exists(migratedPath))
                File.SetAttributes(migratedPath, File.GetAttributes(migratedPath) | FileAttributes.Hidden);
        }
        catch (IOException)
        {
            File.SetAttributes(_legacyTemporaryPath,
                File.GetAttributes(_legacyTemporaryPath) | FileAttributes.Hidden);
        }
        catch (UnauthorizedAccessException)
        {
            try
            {
                File.SetAttributes(_legacyTemporaryPath,
                    File.GetAttributes(_legacyTemporaryPath) | FileAttributes.Hidden);
            }
            catch { }
        }
    }

    private async Task<FileStream> AcquireProcessLockAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.Asynchronous);
                File.SetAttributes(_lockPath, File.GetAttributes(_lockPath) | FileAttributes.Hidden);
                return stream;
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new IOException("Outro processo está gravando o histórico. A espera segura de 5 segundos foi excedida.");
    }

    private void TryDeleteLockFile()
    {
        try
        {
            if (File.Exists(_lockPath)) File.Delete(_lockPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
