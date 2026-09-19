using System.Text.RegularExpressions;
using System.Text;
using Organiza.Application.Abstractions;
using Organiza.Domain.Files;
using Organiza.Domain.MasterBook;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed partial class DocumentAnalysisService(
    IHashCalculator hashCalculator,
    IDocumentTextExtractor documentTextExtractor)
{
    private const int MaximumParallelDocuments = 2;

    public async Task<IReadOnlyList<DocumentAnalysis>> AnalyzeAsync(
        IReadOnlyList<FileItem> files,
        CancellationToken cancellationToken = default,
        IProgress<MasterBookProgress>? progress = null)
    {
        if (files.Count == 0) return [];
        var result = new DocumentAnalysis[files.Count];
        var completed = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), new ParallelOptions
        {
            MaxDegreeOfParallelism = MaximumParallelDocuments,
            CancellationToken = cancellationToken
        }, async (index, token) =>
        {
            var file = files[index];
            progress?.Report(new(Volatile.Read(ref completed), files.Count,
                $"Lendo {Path.GetFileName(file.FullPath)}"));
            var hash = await hashCalculator.ComputeSha256Async(file.FullPath, token);
            var extracted = await documentTextExtractor.ExtractAsync(file.FullPath, token);
            var text = NormalizeWithoutLoss(extracted.Text);
            var identifiers = ProcessNumberRegex().Matches(text).Cast<Match>()
                .Select(match => match.Value)
                .Concat(ProcessClassIdentifierRegex().Matches(text).Cast<Match>().Select(match => match.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var dates = DateRegex().Matches(text).Cast<Match>()
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result[index] = new(file, hash, text, extracted.Method, identifiers, dates,
                extracted.PageCount, extracted.PagesWithText, extracted.OcrPages,
                extracted.IsComplete, extracted.Warnings ?? []);
            var done = Interlocked.Increment(ref completed);
            var method = extracted.OcrPages > 0 ? "Leitura integral + OCR concluída" : "Leitura integral concluída";
            progress?.Report(new(done, files.Count, $"{method}: {Path.GetFileName(file.FullPath)}"));
        });

        return result;
    }

    private static string NormalizeWithoutLoss(string value)
    {
        return WhitespaceRegex().Replace(value.Normalize(NormalizationForm.FormC), " ").Trim();
    }

    [GeneratedRegex(@"\b\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}\b")]
    private static partial Regex ProcessNumberRegex();

    [GeneratedRegex(@"\b[A-Za-z]{2,12}_\d{6,}(?:[-.]\d+)*\b")]
    private static partial Regex ProcessClassIdentifierRegex();

    [GeneratedRegex(@"\b(?:0?[1-9]|[12]\d|3[01])[/.-](?:0?[1-9]|1[0-2])[/.-](?:19|20)\d{2}\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
