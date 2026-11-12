using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ProfileCore;

namespace ProfileUI;

/// Design flow 6, in two steps: a preview, then Copy. Nothing is written until Copy, and everything
/// shown is what Copy will do.
public partial class CopySetupView : FlowBase
{
    static readonly (string Title, CopyKind[] Kinds)[] GroupsInOrder =
    [
        ("MCP SERVERS", [CopyKind.McpServer]),
        ("SETTINGS", [CopyKind.Permissions, CopyKind.Model, CopyKind.Env, CopyKind.Hook]),
        ("SKILLS, AGENTS, COMMANDS, CLAUDE.MD", [CopyKind.Skill, CopyKind.Agent, CopyKind.Command, CopyKind.ClaudeMd]),
        ("PLUGINS", [CopyKind.Plugin]),
    ];

    string? sourceId;
    string? targetId;
    bool filling;
    CopyPlan? shown;

    public CopySetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => ChooseDefaults();
    }

    void ChooseDefaults()
    {
        sourceId = Model.DefaultCopySource();
        targetId = AppModel.CopyTarget(sourceId, null, Model.CopyTargets);
        FillBoxes();
        Reload();
    }

    void FillBoxes()
    {
        filling = true;
        FromBox.ItemsSource = Model.CopySources;
        FromBox.SelectedValue = sourceId;
        ToBox.ItemsSource = Model.CopyTargets.Where(t => t.Id != sourceId).ToList();
        ToBox.SelectedValue = targetId;
        filling = false;
    }

    void FromChanged(object sender, SelectionChangedEventArgs e)
    {
        if (filling || FromBox.SelectedValue is not string id || id == sourceId) return;
        sourceId = id;
        targetId = AppModel.CopyTarget(sourceId, targetId, Model.CopyTargets);
        FillBoxes();
        Reload();
    }

    void ToChanged(object sender, SelectionChangedEventArgs e)
    {
        if (filling || ToBox.SelectedValue is not string id || id == targetId) return;
        targetId = id;
        Reload();
    }

    void Reload() => _ = Model.PreparePreview(sourceId, targetId);

    string TargetName => targetId is { } t ? Model.ProfileById(t)?.DisplayName ?? "the target" : "the target";

    protected override void Update()
    {
        var anyTarget = Model.CopyTargets.Count > 0;
        NoTargets.Visibility = anyTarget ? Visibility.Collapsed : Visibility.Visible;
        Copying.Visibility = anyTarget ? Visibility.Visible : Visibility.Collapsed;
        if (!anyTarget) return;

        var plan = Model.CopyPlan;
        var forThis = plan != null && plan.SourceId == sourceId && plan.TargetId == targetId;
        BlockedText.Text = forThis ? plan!.BlockedReason ?? "" : "";
        BlockedBox.Visibility = BlockedText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var noTarget = targetId == null;
        Loading.Visibility = !forThis && !noTarget && Model.ErrorText == null ? Visibility.Visible : Visibility.Collapsed;
        NothingText.Text = noTarget
            ? $"There is nothing to copy {Model.ProfileById(sourceId ?? "")?.DisplayName ?? "this profile"} into, because the built-in profile can only be copied from. Add another profile, or choose a different profile in From."
            : forThis && plan!.Items.Count == 0 ? "Nothing to copy from this profile." : "";
        NothingText.Visibility = NothingText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!ReferenceEquals(shown, forThis ? plan : null))
        {
            shown = forThis ? plan : null;
            BuildGroups();
        }
        ShowResults();

        var email = targetId is { } t ? Model.ProfileById(t)?.Email : null;
        AccountNote.Text = "Not copyable: claude.ai connectors and account-synced plugins belong to the account. " +
                           $"Connect them at claude.ai while signed in as {email ?? "the target account"}.";
        var n = shown?.TickedCount ?? 0;
        ApplyButton.Content = Model.CopyBusy ? "Copying…" : n == 1 ? "Copy 1 item" : $"Copy {n} items";
        ApplyButton.IsEnabled = !Model.CopyBusy && shown != null && shown.BlockedReason == null && n > 0;
        RestoreButton.IsEnabled = !Model.CopyBusy && targetId != null;
    }

    void ShowResults()
    {
        string? text = null;
        var removals = new List<string>();
        var ok = true;
        if (Model.CopyResult is { } r)
        {
            var backup = r.BackupId == null ? "" : @" A backup is in .claude-profiles\backups.";
            if (r.Failed.Count == 0) text = $"Copied {Items(r.Done.Count)}.{backup}";
            else
            {
                ok = false;
                text = $"Stopped after {Items(r.Done.Count)}. {r.Failed[0].ItemId}: {r.Failed[0].Reason} Nothing after it was copied.{backup}";
            }
            removals.AddRange(r.RemovalCommands);
        }
        else if (Model.RestoreResult is { } res)
        {
            text = $"Restored {res.Restored.Count} and deleted {res.Removed.Count} file{(res.Removed.Count == 1 ? "" : "s")}." +
                   (res.LeftAlone.Count == 0 ? "" : $" Left alone because they changed since: {string.Join(", ", res.LeftAlone.Select(Path.GetFileName))}.");
            removals.AddRange(res.RemovalCommands);
        }
        ResultBox.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        ResultBox.Style = (Style)FindResource(ok ? "Success" : "Critical");
        ResultBox.Margin = new Thickness(0, 10, 0, 0);
        ResultText.Text = text ?? "";
        Removals.Visibility = removals.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RemovalText.Text = string.Join(Environment.NewLine, removals);
    }

    static string Items(int n) => n == 1 ? "1 item" : $"{n} items";

    // ---- The list

    void BuildGroups()
    {
        Groups.Children.Clear();
        if (shown == null) return;
        foreach (var (title, kinds) in GroupsInOrder)
        {
            var items = shown.Items.Where(i => kinds.Contains(i.Kind)).ToList();
            if (items.Count == 0) continue;
            Groups.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0, 10, 0, 2) });
            foreach (var item in items) Groups.Children.Add(Row(item));
        }
    }

    UIElement Row(CopyItem item)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        var head = new DockPanel();
        var badge = new TextBlock
        {
            Text = item.Action.ToString(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource(item.Action switch
            {
                CopyAction.Add => "SystemFillColorSuccessBrush",
                CopyAction.Change => "AccentTextFillColorPrimaryBrush",
                _ => "TextFillColorSecondaryBrush",
            }),
        };
        DockPanel.SetDock(badge, Dock.Right);
        head.Children.Add(badge);
        var box = new CheckBox
        {
            Content = new TextBlock { Text = item.Key, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis },
            IsChecked = item.Ticked, IsEnabled = item.CanBeTicked,
        };
        AutomationProperties.SetName(box, item.Key);
        head.Children.Add(box);
        panel.Children.Add(head);

        var indent = new Thickness(28, 0, 0, 0);
        if (item.Detail.Length > 0) panel.Children.Add(Note(item.Detail, "TextFillColorSecondaryBrush", indent));
        foreach (var w in Warnings(item)) panel.Children.Add(Note(w, "SystemFillColorCautionBrush", indent));
        var choices = new StackPanel { Margin = indent };
        panel.Children.Add(choices);
        Choices(choices, item);

        // Checked and Unchecked, not Click: UI Automation, as screen readers use, toggles without a click.
        // Only the choices under the box are rebuilt, so keyboard focus stays on the box.
        void Toggled(object s, RoutedEventArgs e)
        {
            item.Ticked = box.IsChecked == true;
            Choices(choices, item);
            Model.CopyPlanEdited();
        }
        box.Checked += Toggled;
        box.Unchecked += Toggled;
        return panel;
    }

    /// The secret and conflict choices, shown only while the item is ticked.
    void Choices(StackPanel panel, CopyItem item)
    {
        panel.Children.Clear();
        if (item.Ticked)
        {
            foreach (var field in item.Secrets)
            {
                var s = new CheckBox { Content = $"Include {field.Name} = {SecretDetector.Mask}", IsChecked = field.Ticked, FontSize = 12 };
                s.Checked += (_, _) => { field.Ticked = true; Model.CopyPlanEdited(); };
                s.Unchecked += (_, _) => { field.Ticked = false; Model.CopyPlanEdited(); };
                panel.Children.Add(s);
            }
            if (item.IsFile && item.Action == CopyAction.Change)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = "If it exists:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                foreach (var c in Enum.GetValues<CopyConflict>())
                {
                    var radio = new RadioButton { Content = c.ToString(), GroupName = "conflict:" + item.Id, IsChecked = item.Conflict == c, FontSize = 12, MinWidth = 0, Margin = new Thickness(0, 0, 12, 0) };
                    radio.Checked += (_, _) => { item.Conflict = c; Model.CopyPlanEdited(); };
                    row.Children.Add(radio);
                }
                panel.Children.Add(row);
            }
        }
    }

    TextBlock Note(string text, string brush, Thickness margin) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = margin,
        Foreground = (System.Windows.Media.Brush)FindResource(brush),
    };

    IEnumerable<string> Warnings(CopyItem item)
    {
        if (item.Secrets.Count > 0)
            yield return $"May contain a secret. Unticked by default. Copying puts any ticked value in plain text in {TargetName}. " +
                         "While it is being added, other programs you run can read it from Claude Code's command line.";
        if (item.FlaggedFile) yield return "This looks like it contains a secret. Unticked by default. Check it before copying.";
        if (item.PointsIntoSource) yield return "It points into the source profile's folder, so it would keep depending on that folder.";
        if (item.NeedsReLogin) yield return $"Uses OAuth sign-in. You will need to sign in again in {TargetName}.";
    }

    // ---- Buttons

    void Apply(object sender, RoutedEventArgs e) => _ = Model.ApplyCopy();

    void OpenRestore(object sender, RoutedEventArgs e)
    {
        RestoreMenu.Items.Clear();
        if (Model.Backups.Count == 0) RestoreMenu.Items.Add(new MenuItem { Header = "No backups yet", IsEnabled = false });
        foreach (var b in Model.Backups)
        {
            var item = new MenuItem { Header = b.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) };
            var id = b.Id;
            item.Click += (_, _) => { if (targetId is { } t) _ = Model.RestoreBackup(id, t, sourceId); };
            RestoreMenu.Items.Add(item);
        }
        RestoreMenu.PlacementTarget = RestoreButton;
        RestoreMenu.IsOpen = true;
    }

    void Done(object sender, RoutedEventArgs e)
    {
        Model.ClearCopy();
        Home(sender, e);
    }
}
