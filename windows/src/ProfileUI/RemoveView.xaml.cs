using System.Windows;

namespace ProfileUI;

/// Asks before removing a profile (design flow 7). Cancel is the default button, as for Stop.
public partial class RemoveView : FlowBase
{
    readonly string profileId;

    public RemoveView(string profileId)
    {
        this.profileId = profileId;
        InitializeComponent();
    }

    protected override void Update()
    {
        var p = Model.ProfileById(profileId);
        Ask.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
        Gone.Visibility = p == null ? Visibility.Visible : Visibility.Collapsed;
        if (p == null) return;
        var live = Model.HasLiveSession(profileId);
        TitleText.Text = $"Remove {p.DisplayName}?";
        LiveBox.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        ExplainText.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        ExplainText.Text = $"This signs {p.DisplayName} out, deletes its login file, and moves its folder, history and backups to the Recycle Bin. Other profiles stay signed in.";
        DefaultText.Visibility = p.Id == Model.DefaultProfileId ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = !live && !Model.RemoveBusy;
    }

    void Remove(object sender, RoutedEventArgs e) => _ = Model.RemoveProfile(profileId);

    void Cancel(object sender, RoutedEventArgs e)
    {
        Model.ErrorText = null;
        Model.Route = new Route.Settings(profileId);
    }
}
