using System.Text.Json;
using System.Windows;
using CampusManager.App.Services;
using CampusManager.Core.Sync;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CampusManager.App.Views;

/// <summary>
/// Fenêtre de connexion SSO. Elle ne sert QU'À obtenir un jeton : on charge la page de connexion UPEC,
/// on n'injecte rien et on ne lit pas la saisie, on intercepte seulement la redirection finale
/// moodlemobile://token=… Le moteur web est détruit dès la fin (ses processus sont libérés).
/// </summary>
public sealed class LoginWindow : Window
{
    readonly string _base;
    readonly string _passport = Sso.NewPassport();
    readonly Action<string> _log;
    readonly TaskCompletionSource<string> _done = new();
    readonly WebView2 _web = new();
    int _relaunches;

    LoginWindow(string baseUrl, Action<string> log)
    {
        _base = baseUrl;
        _log = log;
        Title = "Connexion EPREL (UPEC)";
        Width = 520; Height = 740;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Style = (Style)Application.Current.FindResource("CmWindow");
        Content = _web;
        SourceInitialized += (_, _) => Dwm.DarkTitleBar(this, App.Services?.Setting("uiTheme") != "light");
        Loaded += async (_, _) => await Start();
        Closed += (_, _) =>
        {
            _web.Dispose();
            _done.TrySetException(new Exception("Connexion annulée"));
        };
    }

    public static Task<string> LoginAsync(string baseUrl, Action<string> log)
    {
        var w = new LoginWindow(baseUrl, log);
        w.Show();
        w.Activate();
        return w._done.Task;
    }

    /// <summary>Journal : seulement origine + chemin des pages web. Une URL d'application contient le jeton : jamais écrite.</summary>
    static string PathOf(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.Scheme is "https" or "http" ? $"{u.GetLeftPart(UriPartial.Authority)}{u.AbsolutePath[..Math.Min(60, u.AbsolutePath.Length)]}" : "(redirection vers l'application)";
        }
        catch { return "?"; }
    }

    async Task Start()
    {
        try
        {
            var dataDir = Path.Combine(App.Services.DataDir, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception e)
        {
            _log($"  ✘ WebView2 indisponible : {e.Message}");
            Finish(() => _done.TrySetException(new Exception("Le moteur WebView2 de Windows est indisponible")));
            return;
        }
        var core = _web.CoreWebView2;
        core.NavigationStarting += (_, e) =>
        {
            if (Sso.IsTokenRedirect(e.Uri)) { e.Cancel = true; OnToken(e.Uri); return; }
            _log($"  → {PathOf(e.Uri)}");
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Sso.IsTokenRedirect(e.Uri)) OnToken(e.Uri);
        };
        core.LaunchingExternalUriScheme += (_, e) =>
        {
            e.Cancel = true;
            if (Sso.IsTokenRedirect(e.Uri)) OnToken(e.Uri);
        };
        core.NavigationCompleted += async (_, e) =>
        {
            if (_done.Task.IsCompleted) return;
            var url = core.Source;
            Uri u;
            try { u = new Uri(url); } catch { return; }
            _log($"  page : {u.GetLeftPart(UriPartial.Authority)}{u.AbsolutePath}{(e.IsSuccess ? "" : $" ✘ {e.WebErrorStatus}")}");
            var origin = new Uri(_base).GetLeftPart(UriPartial.Authority);
            if (u.GetLeftPart(UriPartial.Authority) == origin && !u.AbsolutePath.StartsWith("/login") && !u.AbsolutePath.Contains("/mobile/launch") && _relaunches < 2)
            {
                // Connecté mais resté sur le tableau de bord : on réclame le jeton.
                _relaunches++;
                _log("  connecté : demande du jeton…");
                core.Navigate(Sso.BuildLaunchUrl(_base, _passport));
                return;
            }
            if (!u.AbsolutePath.Contains("/mobile/launch")) return;
            // Si Moodle affiche une page au lieu de rediriger, on cherche le lien du jeton dans la page.
            try
            {
                var json = await core.ExecuteScriptAsync($"(document.querySelector('a[href^=\"{Sso.UrlScheme}://\"]') || {{}}).href || null");
                var link = JsonSerializer.Deserialize<string?>(json);
                _log($"  page jeton : lien {(link != null ? "oui" : "non")}");
                if (link != null) OnToken(link);
            }
            catch { /* page déjà quittée */ }
        };
        core.Navigate(Sso.BuildLaunchUrl(_base, _passport));
    }

    void OnToken(string url)
    {
        try
        {
            var (token, _) = Sso.ParseTokenUrl(url, _base, _passport);
            Finish(() => _done.TrySetResult(token));
        }
        catch (Exception e) { Finish(() => _done.TrySetException(e)); }
    }

    void Finish(Action complete)
    {
        complete();
        Dispatcher.BeginInvoke(Close);
    }
}
