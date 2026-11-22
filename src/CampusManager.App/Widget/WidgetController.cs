using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CampusManager.App.Services;
using CampusManager.Core.Queries;

namespace CampusManager.App.Widget;

/// <summary>
/// Widget de bureau : réglages, création/destruction de la fenêtre, masquage en plein écran.
/// Il vit indépendamment de la fenêtre principale et ne lit que la base locale.
/// </summary>
public sealed class WidgetController
{
    readonly AppServices _s;
    WidgetSettings _settings;
    WidgetWindow? _win;
    readonly DispatcherTimer _fsTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    bool _hiddenForFullscreen;

    public event Action<WidgetSettings>? SettingsChanged;

    public WidgetController(AppServices s)
    {
        _s = s;
        _settings = WidgetSettings.FromJson(s.Setting("widget"));
        _fsTimer.Tick += (_, _) => CheckFullscreen();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveBoundsNow(); };
    }

    public WidgetSettings Get() => _settings.Clone();
    public bool IsVisible => _win is { IsVisible: true };
    public IntPtr Handle => _win != null ? new WindowInteropHelper(_win).Handle : IntPtr.Zero;

    public WidgetSettings Update(Action<WidgetSettings> patch)
    {
        var before = _settings.Clone();
        patch(_settings);
        _settings.Opacity = Math.Clamp(double.IsFinite(_settings.Opacity) ? _settings.Opacity : 0, 0, 1);
        _s.SetSetting("widget", _settings.ToJson());
        if (_settings.Enabled && _win == null) Create();
        if (!_settings.Enabled && _win != null) Destroy();
        if (_win != null)
        {
            if (before.ClickThrough != _settings.ClickThrough) _win.SetClickThrough(_settings.ClickThrough);
            if (before.Collapsed != _settings.Collapsed) ApplyCollapsed();
            _win.ApplySettings(_settings);
        }
        WatchFullscreen();
        SettingsChanged?.Invoke(Get());
        return Get();
    }

    /// <summary>Le thème de l'appli a changé : le widget garde le sien (rien à faire), méthode gardée pour l'appelant.</summary>
    public void ApplyAppTheme() { }

    Rect Bounds()
    {
        Rect? saved = null;
        try
        {
            if (_s.Setting("widgetBounds") is { } raw)
            {
                var b = JsonSerializer.Deserialize<JsonElement>(raw);
                saved = new Rect(b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(), b.GetProperty("width").GetDouble(), b.GetProperty("height").GetDouble());
            }
        }
        catch { /* réglage illisible : position par défaut */ }
        var v = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return WidgetBounds.Resolve(saved, [v], SystemParameters.WorkArea);
    }

    internal void SaveBounds() { if (!_settings.Collapsed) { _saveTimer.Stop(); _saveTimer.Start(); } }

    void SaveBoundsNow()
    {
        if (_win == null || _settings.Collapsed) return;
        _s.SetSetting("widgetBounds", JsonSerializer.Serialize(new { x = _win.Left, y = _win.Top, width = _win.Width, height = _win.Height }));
    }

    public void Create()
    {
        if (_win != null) return;
        var b = Bounds();
        _win = new WidgetWindow(this, _s, _settings)
        {
            Left = b.X, Top = b.Y, Width = b.Width,
            Height = _settings.Collapsed ? WidgetBounds.CollapsedHeight : b.Height,
        };
        _win.Show();
        WatchFullscreen();
    }

    void ApplyCollapsed()
    {
        if (_win == null) return;
        if (_settings.Collapsed) { SaveBoundsNow(); _win.Height = WidgetBounds.CollapsedHeight; }
        else _win.Height = Bounds().Height;
    }

    public void Resize(double width, double height)
    {
        if (_win == null || _settings.Collapsed || !double.IsFinite(width) || !double.IsFinite(height)) return;
        _win.Width = Math.Round(Math.Clamp(width, 260, 900));
        _win.Height = Math.Round(Math.Clamp(height, 160, 1400));
        SaveBounds();
    }

    /// <summary>Renvoie le widget au niveau du bureau (Windows remonte toutes les fenêtres d'une appli activée).</summary>
    public void SendBack()
    {
        if (_win == null) return;
        var h = Handle;
        Win32.SendToBottom(h);
        _win.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Win32.SendToBottom(h));
    }

    public void Destroy()
    {
        _fsTimer.Stop();
        _win?.CloseForReal();
        _win = null;
    }

    public void Hide() => _win?.Hide();
    public void Show()
    {
        if (_win == null) Create();
        else { _win.Show(); SendBack(); }
    }

    /// <summary>Masque le widget quand une application passe en plein écran (jeu, vidéo…).</summary>
    void WatchFullscreen()
    {
        var want = _settings.Enabled && _settings.AutoHideFullscreen;
        if (!want)
        {
            _fsTimer.Stop();
            if (_hiddenForFullscreen) { _hiddenForFullscreen = false; _win?.Show(); }
            return;
        }
        if (!_fsTimer.IsEnabled) _fsTimer.Start();
    }

    void CheckFullscreen()
    {
        if (_win == null) return;
        var own = Application.Current.Windows.OfType<Window>().Select(w => new WindowInteropHelper(w).Handle).ToList();
        var fs = Win32.IsForegroundFullscreen(own);
        if (fs && _win.IsVisible) { _hiddenForFullscreen = true; _win.Hide(); }
        else if (!fs && _hiddenForFullscreen) { _hiddenForFullscreen = false; _win.Show(); SendBack(); }
    }
}
