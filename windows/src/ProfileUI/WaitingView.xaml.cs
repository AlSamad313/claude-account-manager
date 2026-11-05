using System.Windows;
using ProfileCore;

namespace ProfileUI;

public partial class WaitingView : FlowBase
{
    readonly string profileId;

    public WaitingView(string profileId)
    {
        this.profileId = profileId;
        InitializeComponent();
        Loaded += (_, _) => _ = Model.PollLogin(profileId);
    }

    protected override void Update()
    {
        var p = Model.ProfileById(profileId);
        var state = Model.LoginState;
        var detected = state as LoginState.Detected;
        Detected.Visibility = detected != null ? Visibility.Visible : Visibility.Collapsed;
        Waiting.Visibility = detected == null ? Visibility.Visible : Visibility.Collapsed;
        KeepButton.IsEnabled = !Model.LoginBusy;
        if (detected != null)
        {
            AccountText.Text = $"Account: {detected.Account.Email ?? "unknown"}";
            OrgText.Text = $"Organization: {detected.Account.OrganizationName ?? "unknown"}";
            var other = detected.DuplicateOf is { } d ? Model.ProfileById(d) : null;
            DuplicateBox.Visibility = other != null ? Visibility.Visible : Visibility.Collapsed;
            DuplicateText.Text = other == null ? "" : $"The profile {other.DisplayName} already uses this account. Both would share its usage limits.";
        }
        else if (state is LoginState.TimedOut)
        {
            WaitingTitle.Text = "Still not signed in";
            WaitingText.Text = $"Sign-in for {p?.DisplayName ?? "this profile"} has not finished.";
            BrowserWarning.Visibility = Visibility.Collapsed;
        }
        else
        {
            WaitingTitle.Text = "Waiting for sign-in…";
            WaitingText.Text = $"A terminal window opened running claude-as {p?.CommandName} auth login. Finish signing in there. " +
                               "When it is done, this step changes by itself.";
            BrowserWarning.Visibility = Visibility.Visible;
        }
    }

    void Keep(object sender, RoutedEventArgs e) => _ = Model.KeepLogin(profileId);
    void Retry(object sender, RoutedEventArgs e) => _ = Model.RetryLogin(profileId);
    void Discard(object sender, RoutedEventArgs e) => _ = Model.DiscardLogin(profileId);
}
