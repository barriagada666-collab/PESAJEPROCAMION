using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Main;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Migration;
using PesajeProCamion.Data.Schema;

namespace PesajeProCamion.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Soporte CLI para migración directa desatendida: --migrate "<origen.gdb>" "<destino.fdb>"
        if (args.Length >= 3 && string.Equals(args[0], "--migrate", StringComparison.OrdinalIgnoreCase))
        {
            return RunCliMigrationAsync(args[1], args[2]).GetAwaiter().GetResult();
        }

        bool testMode = args.Any(a => string.Equals(a, "/TEST", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--test", StringComparison.OrdinalIgnoreCase));

        ApplicationConfiguration.Initialize();

        DatabaseSettings settings = AppConfigStore.LoadDatabaseSettings();
        TryEnsureDefaultDatabase(settings);

        using var services = new AppServices(settings, testMode);
        using var login = new LoginForm(services);
        if (login.ShowDialog() != DialogResult.OK || services.UsuarioActual is null)
        {
            return 0;
        }

        using var main = new MainShellForm(services, login.SelectedModuleId);
        Application.Run(main);
        return 0;
    }

    private static void TryEnsureDefaultDatabase(DatabaseSettings settings)
    {
        try
        {
            if (string.Equals(settings.DataSource, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(settings.DatabasePath))
            {
                FirebirdSchemaManager.EnsureDatabaseInitializedAsync(settings).GetAwaiter().GetResult();
            }
        }
        catch (Exception)
        {
            // El usuario puede abrir DatabaseConnectionForm desde el Login si Firebird no responde aún
        }
    }

    private static async Task<int> RunCliMigrationAsync(string gdbPath, string fdbPath)
    {
        var target = new DatabaseSettings
        {
            DataSource = "localhost",
            Port = 3050,
            DatabasePath = Path.GetFullPath(fdbPath),
            UserId = "SYSDBA",
            Password = "masterkey"
        };

        MigrationAuditReport report = await Ods9Migrator.MigrateAsync(gdbPath, target, hashPasswords: true, overwriteTarget: true).ConfigureAwait(false);
        Console.WriteLine($"ODS {report.SourceOdsVersion} -> Firebird OK: {report.TargetDatabase}");
        foreach ((string tbl, int count) in report.TableRowCounts.OrderBy(k => k.Key))
        {
            Console.WriteLine($"  {tbl,-16}: {count}");
        }
        foreach (string finding in report.Findings)
        {
            Console.WriteLine($"  [AUDIT] {finding}");
        }
        return 0;
    }
}
