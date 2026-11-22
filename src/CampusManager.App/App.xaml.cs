using System.Windows;
using System.Windows.Threading;
using CampusManager.App.Services;
using CampusManager.App.Views;
using CampusManager.App.Widget;
using CampusManager.Core.Sync;

namespace CampusManager.App;

/// <summary>
/// Campus Manager : un seul processus.
///  - La fenêtre principale est créée à la demande et FERMÉE (libérée) quand on la ferme.
///  - Le widget et la synchronisation continuent sans elle.
///  - Ctrl+Alt+C rouvre l'interface ; Ctrl+Alt+W affiche/masque le widget ; Ctrl+Alt+X clic à travers.
/// Options : --hidden (sans fenêtre principale), --page=&lt;vue&gt;.
/// Variable CAMPUS_DATA_DIR : dossier de données (tests sur une copie).
/// </summary>
public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    public static WidgetController Widget { get; private set; } = null!;
    public static new App Current => (App)Application.Current;

    SingleInstance? _instance;
    Hotkeys? _hotkeys;
    MainWindow? _main;

    static string? Arg(string[] args, string name) =>
        args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        t0 = System.Diagnostics.Stopwatch.StartNew();
        var page = Arg(e.Args, "page");
        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            SingleInstance.Signal(page ?? "");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ev) => { Services?.Log($"erreur : {ev.Exception}"); ev.Handled = true; };

        var dataDir = Environment.GetEnvironmentVariable("CAMPUS_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Campus Manager");
        Services = new AppServices(dataDir, Environment.GetEnvironmentVariable("CAMPUS_ROOT"), new TokenStore(dataDir),
            login: () => LoginWindow.LoginAsync(AppServices.Eprel, s => Services.Log($"sso {s}")),
            client: token => new MoodleClient(AppServices.Eprel, token),
            checkToken: async token => { await new MoodleClient(AppServices.Eprel, token).SiteInfoAsync(); return true; });

        Trace("services");
        _instance.Listen(msg => Dispatcher.BeginInvoke(() => OpenMain(string.IsNullOrEmpty(msg) ? null : msg)));

        // Démarrage avec Windows activé par défaut (une seule fois) ; désactivable dans Paramètres.
        var auto = Services.Setting("autostart");
        // Essai sur une copie des données (CAMPUS_DATA_DIR) : on ne touche pas au démarrage Windows réel.
        if (Environment.GetEnvironmentVariable("CAMPUS_DATA_DIR") != null) { }
        else if (auto == null)
        {
            var on = Autostart.Set(true);
            // On enregistre le CHOIX (activé par défaut), pas le résultat : un échec sera retenté au prochain lancement.
            Services.SetSetting("autostart", "1");
            Services.Log($"démarrage avec Windows : {(on ? "activé" : "échec, nouvel essai au prochain lancement")}");
        }
        else Autostart.Ensure(auto == "1");

        Trace("autostart");
        Widget = new WidgetController(Services);
        // La fenêtre demandée s'affiche d'abord ; le widget est créé juste après (il coûte ~0,5 s au 1er rendu).
        if (!e.Args.Contains("--hidden")) OpenMain(page);
        Trace("fenetre");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (Widget.Get().Enabled) Widget.Create(); Trace("widget"); });
        // Développement : sélectionner un fichier au lancement (vérifier l'aperçu).
        if (_main != null && long.TryParse(Arg(e.Args, "select"), out var sel)) _main.Vm.Selected = sel;

        _hotkeys = new Hotkeys();
        _hotkeys.Register('C', () => OpenMain());
        _hotkeys.Register('W', () => { if (Widget.IsVisible) Widget.Hide(); else Widget.Show(); });
        // Clic à travers : quand il est actif, le widget ne reçoit plus de clics ; ce raccourci le désactive.
        _hotkeys.Register('X', () => Widget.Update(s => s.ClickThrough = !s.ClickThrough));
    }

    static System.Diagnostics.Stopwatch t0 = new();
    /// <summary>Mesure du démarrage (variable CAMPUS_TRACE) : écrit les étapes dans le journal.</summary>
    public static void Trace(string w) { if (Environment.GetEnvironmentVariable("CAMPUS_TRACE") != null) Services?.Log($"trace {w} {t0.ElapsedMilliseconds} ms"); }

    public void OpenMain(string? page = null)
    {
        if (_main != null)
        {
            if (page != null) _main.Vm.Go(page);
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Show();
            _main.Activate();
            return;
        }
        Trace("avant MainWindow");
        _main = new MainWindow(page ?? "home");
        Trace("MainWindow construite");
        _main.Activated += (_, _) => Widget.SendBack();
        // Fermer = libérer la fenêtre, PAS quitter l'appli.
        _main.Closed += (_, _) =>
        {
            _main = null;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => GC.Collect(2, GCCollectionMode.Aggressive, true, true));
        };
        _main.Show();
    }

    /// <summary>Synchro lancée depuis le widget : fonctionne aussi fenêtre principale fermée.</summary>
    public async void SyncFromWidget()
    {
        if (_main != null) { _main.RunSync(); return; }
        await Services.SyncAsync(false);
    }

    public void QuitAll()
    {
        Widget.Destroy();
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _instance?.Dispose();
        Services?.Dispose();
        base.OnExit(e);
    }
}
