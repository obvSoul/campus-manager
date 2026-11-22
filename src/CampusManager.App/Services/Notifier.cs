using CommunityToolkit.WinUI.Notifications;

namespace CampusManager.App.Services;

/// <summary>Notifications Windows (centre de notifications). Un clic ouvre la page indiquée de Campus Manager.</summary>
public static class Notifier
{
    public static void Show(string title, string body, string page)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("page", page)
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch (Exception e) { App.Services?.Log($"notification impossible : {e.Message}"); }
    }

    /// <summary>Branche le clic sur une notification : ouvre la fenêtre principale sur la bonne page.</summary>
    public static void Listen(Action<string?> open) =>
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            open(args.TryGetValue("page", out var p) ? p : null);
        };
}
