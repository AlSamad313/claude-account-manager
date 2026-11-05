using System.Windows;
using System.Windows.Controls;

namespace ProfileUI;

public partial class AddProfileView : FlowBase
{
    bool busy;

    public AddProfileView()
    {
        InitializeComponent();
        ColorPicker.Build();
        Loaded += (_, _) => NameBox.Focus();
    }

    string Suggested => AppModel.SuggestCommandName(NameBox.Text);
    string CommandName => CommandBox.Text.Length == 0 ? Suggested : CommandBox.Text;

    void NameChanged(object sender, TextChangedEventArgs e) => Update();
    void CommandChanged(object sender, TextChangedEventArgs e) => Update();

    protected override void Update()
    {
        CommandHint.Text = CommandBox.Text.Length == 0 ? (Suggested.Length == 0 ? "work" : Suggested) : "";
        CommandNote.Text = $"Used as claude-as {(CommandName.Length == 0 ? "name" : CommandName)}. It cannot change later.";
        ContinueButton.IsEnabled = !busy && NameBox.Text.Length > 0 && CommandName.Length > 0 && DataContext is AppModel { ShellInstalled: true };
    }

    async void Continue(object sender, RoutedEventArgs e)
    {
        if (!ContinueButton.IsEnabled) return;
        busy = true;
        Update();
        await Model.AddProfile(NameBox.Text, CommandName, ColorPicker.Selected);
        busy = false;
        Update();
    }

    void Cancel(object sender, RoutedEventArgs e)
    {
        Model.ErrorText = null;
        Model.Route = Route.Home;
    }
}
