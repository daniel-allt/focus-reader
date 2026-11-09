namespace FocusReader;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\FocusReader.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("O Focus Reader já está em execução. Procure o ícone na bandeja do sistema.",
                "Focus Reader", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            SystemCursorManager.Restore();
            MessageBox.Show($"Ocorreu um erro inesperado:\n\n{e.Exception.Message}",
                "Focus Reader", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => SystemCursorManager.Restore();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemCursorManager.Restore();

        SystemCursorManager.RecoverFromPreviousRun();
        Application.Run(new TrayApplicationContext());
    }
}
