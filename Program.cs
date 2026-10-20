using System.Text;

namespace FilipCommander;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Pozwala czytać pliki w Windows-1250 w podglądzie (F3).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
