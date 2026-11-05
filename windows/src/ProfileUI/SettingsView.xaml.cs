using System.Windows;
using System.Windows.Input;
using ProfileCore;

namespace ProfileUI;

public partial class SettingsView : FlowBase
{
    readonly string profileId;
    bool filled;

    public SettingsView(string profileId)
    {
        this.profileId = profileId;
        InitializeComponent();
        ColorPicker.Picked += c => { if (Model.ProfileById(profileId) is { } p) _ = Model.SetColor(p, c); };
    }

    protected override void Update()
    {
        var p = Model.ProfileById(profileId);
        Body.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
        Gone.Visibility = p == null ? Visibility.Visible : Visibility.Collapsed;
        if (p == null) return;
        if (!filled)
        {
            // Only once, so a refresh never overwrites what the user is typing.
            NameBox.Text = p.DisplayName;
            filled = true;
        }
        if (ColorPicker.Selected != p.Color || ColorPicker.Children.Count == 0) ColorPicker.Selected = p.Color;
        CommandText.Text = $"Command: claude-as {p.CommandName}. Fixed when the profile was created. Renaming changes only the display name; the folder and sign-in stay the same.";
        AccountText.Text = $"Account: {p.Email ?? "not signed in"}";
        BuiltinText.Visibility = p.Builtin ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Visibility = p.Builtin ? Visibility.Collapsed : Visibility.Visible;
        VSCodeButton.Content = $"Open VS Code on {p.DisplayName}";
        DefaultButton.Visibility = p.Id != Model.DefaultProfileId && p.State == ProfileState.Ready ? Visibility.Visible : Visibility.Collapsed;
    }

    void SaveName()
    {
        if (Model.ProfileById(profileId) is { } p && NameBox.Text != p.DisplayName) _ = Model.Rename(p, NameBox.Text);
    }

    void NameKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SaveName();
        e.Handled = true;
    }

    void OpenVSCode(object sender, RoutedEventArgs e) { if (Model.ProfileById(profileId) is { } p) _ = Model.OpenVSCode(p); }
    void MakeDefault(object sender, RoutedEventArgs e) { if (Model.ProfileById(profileId) is { } p) _ = Model.SetDefault(p); }

    void Remove(object sender, RoutedEventArgs e)
    {
        SaveName();
        Model.ErrorText = null;
        Model.Route = new Route.Remove(profileId);
    }

    void Done(object sender, RoutedEventArgs e)
    {
        SaveName();
        Model.Route = Route.Home;
    }
}
