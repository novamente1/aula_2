using System.Diagnostics;
using Organiza.Application.Services;

namespace Organiza.Wpf.Services;

public sealed record DocumentPreviewLaunchResult(bool Opened, string Viewer, string? Error = null);

public static class DocumentPreviewLauncher
{
    public static DocumentPreviewLaunchResult Open(string filePath)
    {
        if (!File.Exists(filePath))
            return new(false, string.Empty, "O arquivo original não foi encontrado.");

        try
        {
            if (DocumentPreviewPolicy.ShouldUseBrowser(filePath))
            {
                var browser = FindBrowser();
                if (browser is not null)
                {
                    var profileDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Organiza", "Visualizador");
                    Directory.CreateDirectory(profileDirectory);
                    var startInfo = new ProcessStartInfo(browser.Path) { UseShellExecute = false };
                    startInfo.ArgumentList.Add($"--user-data-dir={profileDirectory}");
                    startInfo.ArgumentList.Add("--disable-extensions");
                    startInfo.ArgumentList.Add("--no-first-run");
                    startInfo.ArgumentList.Add("--no-default-browser-check");
                    startInfo.ArgumentList.Add("--new-window");
                    startInfo.ArgumentList.Add(DocumentPreviewPolicy.ToLocalFileUri(filePath));
                    var process = Process.Start(startInfo);
                    if (process is null)
                        return new(false, string.Empty, $"Não foi possível iniciar o {browser.DisplayName}.");
                    return new(true, browser.DisplayName);
                }

                if (string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
                    return new(false, string.Empty,
                        "Nenhum navegador compatível foi encontrado. O PDF não foi enviado ao Adobe para evitar telas de compra.");
            }

            var officeViewer = FindOfficeViewer(filePath);
            if (officeViewer is not null)
            {
                var startInfo = new ProcessStartInfo(officeViewer.Path) { UseShellExecute = false };
                foreach (var argument in officeViewer.Arguments) startInfo.ArgumentList.Add(argument);
                startInfo.ArgumentList.Add(Path.GetFullPath(filePath));
                var process = Process.Start(startInfo);
                if (process is null)
                    return new(false, string.Empty, $"Não foi possível iniciar o {officeViewer.DisplayName}.");
                return new(true, officeViewer.DisplayName);
            }

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return new(true, "aplicativo padrão do Windows");
        }
        catch (Exception exception)
        {
            return new(false, string.Empty, exception.Message);
        }
    }

    private static BrowserInstallation? FindBrowser()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            new BrowserInstallation("Microsoft Edge", Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe")),
            new BrowserInstallation("Microsoft Edge", Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")),
            new BrowserInstallation("Google Chrome", Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe")),
            new BrowserInstallation("Google Chrome", Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe")),
            new BrowserInstallation("Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"))
        };
        return candidates.FirstOrDefault(candidate => File.Exists(candidate.Path));
    }

    private static ViewerInstallation? FindOfficeViewer(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.ToLowerInvariant() is not (".doc" or ".docx" or ".odt" or ".xls" or ".xlsx" or ".ods"))
            return null;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var libreOfficeCandidates = new[]
        {
            Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe"),
            Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.exe")
        };
        var libreOffice = libreOfficeCandidates.FirstOrDefault(File.Exists);
        if (libreOffice is null) return null;
        var mode = extension.ToLowerInvariant() is ".xls" or ".xlsx" or ".ods" ? "--calc" : "--writer";
        return new("LibreOffice", libreOffice, ["--nologo", mode]);
    }

    private sealed record BrowserInstallation(string DisplayName, string Path);
    private sealed record ViewerInstallation(string DisplayName, string Path, IReadOnlyList<string> Arguments);
}
