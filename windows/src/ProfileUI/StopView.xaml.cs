using System.Windows;
using ProfileCore;

namespace ProfileUI;

public partial class StopView : FlowBase
{
    readonly string sessionId;
    // Held, so the row cannot change under the question while the list refreshes.
    SessionRow? held;

    public StopView(string sessionId)
    {
        this.sessionId = sessionId;
        InitializeComponent();
    }

    protected override void Update()
    {
        held ??= Model.Row(sessionId) is { Live: true } r ? r : null;
        Ask.Visibility = held != null ? Visibility.Visible : Visibility.Collapsed;
        Gone.Visibility = held == null ? Visibility.Visible : Visibility.Collapsed;
        if (held == null) return;
        TitleText.Text = held.Title;
        DetailText.Text = $"{Model.ProfileById(held.ProfileId)?.DisplayName ?? "Unknown"} · {held.Cwd ?? "unknown folder"} · {held.Status ?? "status unknown"}";
        var warning = new StopRequest(held.Title, held.Cwd, held.Status, held.Pid ?? 0).Warning;
        WarningBox.Visibility = warning != null ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = warning ?? "";
        StopButton.IsEnabled = !Model.StopBusy;
    }

    void Stop(object sender, RoutedEventArgs e)
    {
        if (held != null) _ = Model.ConfirmStop(held);
    }
}
