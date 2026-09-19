using System.Text;
using System.Windows.Threading;

namespace Organiza.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteDiagnosticLog(e.Exception);
        MessageBox.Show(
            "O Organiza encontrou um erro inesperado, registrou o diagnóstico e permaneceu aberto. " +
            "Nenhum arquivo será alterado sem uma nova confirmação.\n\n" +
            $"Detalhe: {e.Exception.Message}",
            "Erro tratado pelo Organiza", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteDiagnosticLog(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Organiza", "Logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "diagnosticos.log");
            var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Exceção não tratada na interface.{Environment.NewLine}" +
                        $"{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch
        {
            // Uma falha no log nunca deve derrubar a interface nem ocultar a mensagem ao usuário.
        }
    }
}
