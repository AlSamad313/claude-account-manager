using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProfileUI;

/// One task in the manual, as on macOS, in Windows words. Button names here must match the ones on screen.
public sealed record GuideTopic(string Icon, string Title, string Summary, string[] Steps, string? Tip = null, (string Command, string Meaning)[]? Commands = null)
{
    public static readonly GuideTopic[] All =
    [
        new("", "Add an account", "Sign in once, and it stays signed in.",
            ["Press Add profile…", "Choose a name, command and colour.", "Sign in from the terminal window that opens.", "Press Keep."],
            "Use a private browser window to pick the right account."),
        new("", "Change the default", "Choose the account plain claude uses.",
            ["Press Make default on a profile."],
            "The tray icon shows the default's colour. Running sessions don't change."),
        new("", "Start a session", "Open a terminal window on any account.",
            ["Press New Terminal on a profile.", "Choose a folder, then press Select Folder."]),
        new("", "Resume a session", "Continue on the right account, in the same folder.",
            ["Find it under Recent sessions, or use the filter.", "Press Resume."]),
        new("", "Stop a session", "End a running session.",
            ["Press Stop.", "Press Stop session to confirm."],
            "Its window goes back to its shell, or closes if the app opened it."),
        new("", "Open VS Code", "Use an account in VS Code.",
            ["Close VS Code if it's running.", "Press ⋯ on a profile, then Open VS Code."]),
        new("", "Copy setup", "Copy MCP servers, skills and settings to a profile.",
            ["Press Copy setup…", "Choose From and To.", "Tick what you want.", "Press Copy."],
            "Secrets start unticked. To undo a copy, use Restore…"),
        new("", "Rename a profile", "Change a profile's name or colour.",
            ["Press ⋯ on a profile, then Settings…", "Change the name or colour."],
            "Its command stays the same."),
        new("", "Fix a problem", "Check that everything is set up.",
            ["Press Doctor.", "Press Repair files if it appears."]),
        new("", "Terminal commands", "The same actions, typed.", [],
            Commands: [("claude", "default profile"), ("claude-as <name>", "chosen profile"), ("cpm list", "all sessions"),
                       ("cpm new <name>", "start a session"), ("cpm resume <id>", "resume a session"), ("cpm stop <id>", "stop a session"),
                       ("cpm default <name>", "change the default"), ("cpm doctor", "check the setup")]),
        new("", "Good to know", "What this app doesn't do.", [],
            "The desktop app and Cowork stay on their own login. The app starts with Windows unless you turn that off in its tray menu."),
    ];
}

/// The Guide: every topic folded, one open at a time.
public partial class GuideView : FlowBase
{
    public GuideView()
    {
        InitializeComponent();
        foreach (var t in GuideTopic.All) Topics.Children.Add(Topic(t));
    }

    Expander Topic(GuideTopic t)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(new TextBlock
        {
            Text = t.Icon, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Secondary,
        });
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = t.Title, FontWeight = FontWeights.SemiBold });
        words.Children.Add(new TextBlock { Text = t.Summary, FontSize = 12, Foreground = Secondary, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(words, 1);
        header.Children.Add(words);

        var body = new StackPanel { Margin = new Thickness(28, 4, 0, 4) };
        for (var i = 0; i < t.Steps.Length; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var n = new TextBlock { Text = $"{i + 1}", Width = 16, FontWeight = FontWeights.SemiBold, Foreground = Secondary };
            DockPanel.SetDock(n, Dock.Left);
            row.Children.Add(n);
            row.Children.Add(new TextBlock { Text = t.Steps[i], TextWrapping = TextWrapping.Wrap });
            body.Children.Add(row);
        }
        if (t.Commands is { } commands)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < commands.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var cmd = new TextBox
                {
                    Text = commands[i].Command, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                    Padding = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, Margin = new Thickness(0, 2, 12, 2),
                };
                var meaning = new TextBlock { Text = commands[i].Meaning, FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(cmd, i);
                Grid.SetRow(meaning, i);
                Grid.SetColumn(meaning, 1);
                grid.Children.Add(cmd);
                grid.Children.Add(meaning);
            }
            body.Children.Add(grid);
        }
        if (t.Tip is { } tip) body.Children.Add(new TextBlock { Text = tip, FontSize = 12, Foreground = Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        var e = new Expander { Header = header, Content = body, Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(e, t.Title);
        e.Expanded += (_, _) => { foreach (var other in Topics.Children.OfType<Expander>()) if (other != e) other.IsExpanded = false; };
        return e;
    }

    Brush Secondary => TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
}
