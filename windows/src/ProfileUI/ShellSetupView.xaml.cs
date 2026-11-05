using System.Windows;

namespace ProfileUI;

public partial class ShellSetupView : FlowBase
{
    public ShellSetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => Model.ClearInstallResult();
    }

    protected override void Update()
    {
        var r = Model.InstallResult;
        Ask.Visibility = r == null ? Visibility.Visible : Visibility.Collapsed;
        Finished.Visibility = r != null ? Visibility.Visible : Visibility.Collapsed;
        SetUpButton.IsEnabled = !Model.Busy;
        if (r == null) return;
        var lines = new List<string>
        {
            r.PathChanged ? "The bin folder is now first in your user PATH. Programs that were already open keep the old PATH until they are restarted."
                          : "The bin folder was already first in your user PATH.",
        };
        foreach (var f in r.CompletionAdded ?? []) lines.Add($"Added tab completion to {f}.");
        Details.Text = string.Join("\n", lines);
    }

    void SetUp(object sender, RoutedEventArgs e) => _ = Model.SetUpCommandLine(Completion.IsChecked == true);

    void Finish(object sender, RoutedEventArgs e)
    {
        Model.ClearInstallResult();
        Home(sender, e);
    }
}
