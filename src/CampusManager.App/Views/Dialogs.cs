using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CampusManager.App.Services;

namespace CampusManager.App.Views;

/// <summary>Petite fenêtre modale aux couleurs du thème (base des boîtes de saisie et de confirmation).</summary>
public class ThemedDialog : Window
{
    protected readonly StackPanel Body = new();
    protected readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };

    public ThemedDialog(Window? owner, string title, string label)
    {
        Style = (Style)Application.Current.FindResource("CmWindow");
        SetResourceReference(BackgroundProperty, "Panel");
        Owner = owner;
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(W.Text(title, size: 16, weight: FontWeights.SemiBold));
        var l = W.Text(label, "TMuted", 12.5, ellipsis: false);
        l.TextWrapping = TextWrapping.Wrap; l.Margin = new Thickness(0, 10, 0, 10);
        root.Children.Add(l);
        root.Children.Add(Body);
        root.Children.Add(Actions);
        Content = root;
        SourceInitialized += (_, _) => Dwm.DarkTitleBar(this, App.Services?.Setting("uiTheme") != "light");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
    }

    protected Button AddButton(string text, bool primary, Action onClick)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(primary ? "BtnPrimary" : "BtnGhost"), Margin = new Thickness(8, 0, 0, 0), IsDefault = primary };
        b.Click += (_, _) => onClick();
        Actions.Children.Add(b);
        return b;
    }
}

public sealed class PromptDialog : ThemedDialog
{
    string? _result;

    PromptDialog(Window? owner, string title, string label, string initial, List<(string Value, string Label)>? options) : base(owner, title, label)
    {
        Control input;
        if (options == null)
        {
            var tb = new TextBox { Text = initial };
            Loaded += (_, _) =>
            {
                tb.Focus();
                var dot = initial.LastIndexOf('.');
                tb.Select(0, dot > 0 ? dot : initial.Length); // sélection du nom sans l'extension
            };
            input = tb;
        }
        else
        {
            var cb = new ComboBox();
            foreach (var (v, l) in options) cb.Items.Add(new ComboBoxItem { Content = l, Tag = v });
            cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == initial) ?? cb.Items.OfType<ComboBoxItem>().FirstOrDefault();
            Loaded += (_, _) => cb.Focus();
            input = cb;
        }
        Body.Children.Add(input);
        AddButton("Annuler", false, () => DialogResult = false);
        AddButton("Valider", true, () =>
        {
            var v = input is TextBox t ? t.Text.Trim() : ((input as ComboBox)?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(v)) return;
            _result = v; DialogResult = true;
        });
    }

    public static string? Ask(Window? owner, string title, string label, string initial)
    {
        var d = new PromptDialog(owner, title, label, initial, null);
        return d.ShowDialog() == true ? d._result : null;
    }

    public static string? Choose(Window? owner, string title, string label, List<(string Value, string Label)> options, string? initial)
    {
        var d = new PromptDialog(owner, title, label, initial ?? "", options);
        return d.ShowDialog() == true ? d._result : null;
    }
}

public sealed class ConfirmDialog : ThemedDialog
{
    ConfirmDialog(Window? owner, string title, string detail, string ok) : base(owner, title, detail)
    {
        AddButton("Annuler", false, () => DialogResult = false).IsCancel = true;
        AddButton(ok, true, () => DialogResult = true);
    }

    public static bool Ask(Window? owner, string title, string detail, string ok) => new ConfirmDialog(owner, title, detail, ok).ShowDialog() == true;
}
