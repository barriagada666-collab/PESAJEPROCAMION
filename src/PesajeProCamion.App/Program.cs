using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Main;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Migration;
using PesajeProCamion.Data.Schema;
using PesajeProCamion.Domain.Diagnostics;

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

        // Soporte CLI para diagnóstico remoto UNC/TCP: --probe "<ruta_unc_o_host>" [usuario] [clave]
        if (args.Length >= 2 && string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase))
        {
            string u = args.Length >= 3 ? args[2] : "SYSDBA";
            string p = args.Length >= 4 ? args[3] : "masterkey";
            return RunCliProbeAsync(args[1], u, p).GetAwaiter().GetResult();
        }

        bool testMode = args.Any(a => string.Equals(a, "/TEST", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--test", StringComparison.OrdinalIgnoreCase));
        bool noSplash = args.Any(a => string.Equals(a, "--nosplash", StringComparison.OrdinalIgnoreCase));

        ApplicationConfiguration.Initialize();
        RegisterGlobalExceptionHandlers();
        AppLogger.Info("Startup", $"Iniciando PesajeProCamion v2.3.0 (TestMode={testMode}, Portable={AppConfigStore.IsPortableMode}).");

        SplashForm? splash = null;
        if (!noSplash)
        {
            splash = new SplashForm();
            splash.Show();
            splash.UpdateProgress(30, "Cargando configuración local y políticas...");
        }

        DatabaseSettings settings = AppConfigStore.LoadDatabaseSettings();
        splash?.UpdateProgress(65, $"Verificando conexión Firebird ({settings.DataSource}:{settings.Port})...");
        TryEnsureDefaultDatabase(settings);

        using var services = new AppServices(settings, testMode);
        splash?.UpdateProgress(100, "Interfaz corporativa lista.");
        splash?.Close();
        splash?.Dispose();

        using var login = new LoginForm(services);
        if (login.ShowDialog() != DialogResult.OK || services.UsuarioActual is null)
        {
            return 0;
        }

        using var main = new MainShellForm(services, login.SelectedModuleId);
        Application.Run(main);
        return 0;
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatalModal("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLogger.Error("AppDomain", "Excepción crítica no controlada.", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.Error("TaskScheduler", "Excepción asíncrona no observada.", e.Exception);
            e.SetObserved();
        };
    }

    private static void ShowFatalModal(string source, Exception ex)
    {
        AppLogger.Error(source, "Error inesperado en tiempo de ejecución.", ex);
        DialogResult res = MessageBox.Show(
            $"Se produjo un error inesperado:\r\n{ex.Message}\r\n\r\nEl detalle completo se registró en los Logs de Errores.\r\n¿Desea abrir el Visor de Logs ahora?",
            "Control de Errores — PesajeProCamion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);
        if (res == DialogResult.Yes)
        {
            using var dlg = new ErrorLogViewerDialog();
            dlg.ShowDialog();
        }
    }

    private static void TryEnsureDefaultDatabase(DatabaseSettings settings)
    {
        try
        {
            if (string.Equals(settings.DataSource, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(settings.DatabasePath))
                {
                    FirebirdSchemaManager.EnsureDatabaseInitializedAsync(settings).GetAwaiter().GetResult();
                }
                else
                {
                    FirebirdSchemaManager.EnsureBarriagadaAdminAsync(settings).GetAwaiter().GetResult();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", $"BD por defecto no inicializada en arranque: {ex.Message}");
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
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {tbl,-16}: {count}"));
        }
        foreach (string finding in report.Findings)
        {
            Console.WriteLine($"  [AUDIT] {finding}");
        }
        return 0;
    }

    private static async Task<int> RunCliProbeAsync(string rawPath, string user, string password)
    {
        var baseSettings = new DatabaseSettings
        {
            DataSource = "localhost",
            Port = 3050,
            DatabasePath = rawPath,
            UserId = user,
            Password = password
        };
        DatabaseProbeResult res = await RemoteDatabaseResolver.ResolveAndProbeAsync(baseSettings, rawPath, autoDetectDialectAndCharset: true).ConfigureAwait(false);
        foreach (string line in res.AttemptLog)
        {
            Console.WriteLine(line);
        }
        Console.WriteLine(res.Success
            ? $"PROBE_OK host={res.ResolvedSettings.DataSource} db={res.ResolvedSettings.DatabasePath} dialect={res.ResolvedSettings.Dialect} charset={res.ResolvedSettings.Charset} pesajes={res.TotalPesajes}"
            : $"PROBE_FAIL {res.ErrorMessage}");
        return res.Success ? 0 : 2;
    }
}
