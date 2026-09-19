using Organiza.Application.Abstractions;
using Organiza.Application.Services;
using Organiza.Infrastructure.Files;
using Organiza.Infrastructure.Pdf;
using Organiza.Wpf.Services;
using Organiza.Wpf.ViewModels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Organiza.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var state = new WorkspaceState();
        var fileSystem = new PhysicalFileSystem();
        var hash = new Sha256HashCalculator(fileSystem);
        var folders = new StandardFolderManager(fileSystem);
        var pathGuard = new PathGuard();
        var pdfTextExtractor = new PdfFirstPageTextExtractor();
        var documentAnalysis = new DocumentAnalysisService(hash,
            new DeepDocumentTextExtractor(pdfTextExtractor));
        IContentSuggestionGateway suggestions = new ContentAwareLocalSuggestionGateway();

        DataContext = new MainViewModel(
            state,
            new FolderSelectionViewModel(state, folders, fileSystem, hash, new GoogleDriveFolderLinkResolver()),
            new DuplicatesViewModel(state, fileSystem, hash, folders, pathGuard),
            new PdfSplitViewModel(state, fileSystem, hash, folders, pathGuard, new PdfSharpDocumentAdapter()),
            new OrganizeViewModel(state, fileSystem, suggestions, documentAnalysis, hash),
            new HistoryViewModel(state),
            new SettingsViewModel(pdfTextExtractor));

        ConfigureDiagnosticEvidenceMode();
    }

    private void ConfigureDiagnosticEvidenceMode()
    {
        var arguments = Environment.GetCommandLineArgs();
        var previewIndex = Array.FindIndex(arguments,
            item => string.Equals(item, "--diagnostic-preview", StringComparison.OrdinalIgnoreCase));
        if (previewIndex < 0 || previewIndex + 1 >= arguments.Length) return;
        var previewPath = arguments[previewIndex + 1];
        var captureIndex = Array.FindIndex(arguments,
            item => string.Equals(item, "--capture-diagnostic", StringComparison.OrdinalIgnoreCase));
        var capturePath = captureIndex >= 0 && captureIndex + 1 < arguments.Length
            ? arguments[captureIndex + 1]
            : null;
        Width = 1440;
        Height = 900;
        Loaded += async (_, _) =>
        {
            if (DataContext is not MainViewModel main || main.CurrentPage is not FolderSelectionViewModel selection)
                return;
            await selection.LoadDiagnosticPreviewAsync(previewPath);
            if (string.IsNullOrWhiteSpace(capturePath)) return;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            CaptureWindow(capturePath);
            Close();
        };
    }

    private void CaptureWindow(string outputPath)
    {
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }
}
