using Organiza.Domain.Files;
using Organiza.Infrastructure.Pdf;
using Organiza.Wpf.Services;

namespace Organiza.Wpf.ViewModels;

public sealed class SettingsViewModel(PdfFirstPageTextExtractor textExtractor)
{
    public string AppVersion => $"{OrganizaApplicationInfo.DisplayName} — Livro Mestre 360° e organização documental segura";
    public string PdfLimit => textExtractor.PdfRasterAvailable
        ? $"{PdfSafetyLimits.PartSizeBytes / 1024 / 1024} MB por parte; rasterização segura disponível"
        : $"{PdfSafetyLimits.PartSizeBytes / 1024 / 1024} MB por parte; rasterização indisponível";
    public string PathLimits => "Ajuste automático de nomes; aviso acima de 220 e bloqueio absoluto acima de 240 caracteres";
    public string StructureCatalog => "Nova Raiz auditada em 18/09/2026; 15 categorias e perfis reais protegidos";
    public string TextExtractionStatus => textExtractor.NativeTextAvailable
        ? textExtractor.OcrAvailable
            ? "pdftotext ativo; OCR Tesseract ativo como fallback"
            : "pdftotext ativo; OCR indisponível"
        : textExtractor.OcrAvailable
            ? "Extração gerenciada ativa; OCR Tesseract ativo como fallback"
            : "Extração gerenciada ativa para PDFs de até 50 MiB; OCR indisponível";
    public string CodexStatus => "Integração externa desativada nesta versão — análise local baseada no conteúdo";
}
