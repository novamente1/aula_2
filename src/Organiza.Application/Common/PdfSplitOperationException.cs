namespace Organiza.Application.Common;

public sealed class PdfSplitOperationException(
    string sourcePath,
    string userMessage,
    Exception innerException) : Exception(userMessage, innerException)
{
    public string SourcePath { get; } = sourcePath;
}
