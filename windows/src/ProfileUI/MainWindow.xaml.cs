using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProfileUI;

/// The full window: every profile in the sidebar, and every session in a table.
public partial class MainWindow : Window
{
    readonly AppModel model;
    bool restoring;

    public MainWindow(AppModel model)
    {
        this.model = model;
        DataContext = model;
        InitializeComponent();
        model.PropertyChanged += ModelChanged;
        Loaded += (_, _) => { RestoreSidebar(); SearchBox.Focus(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) model.ViewAppeared(); else model.ViewDisappeared(); };
        InputBindings.Add(new KeyBinding(new Command(() => { SearchBox.Focus(); SearchBox.SelectAll(); }), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Command(() => _ = model.Refresh()), Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Command(() => _ = model.NewSessionOnTarget()), Key.T, ModifierKeys.Control));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        model.PropertyChanged -= ModelChanged;
        base.OnClosing(e);
    }

    void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.Sidebar)) RestoreSidebar();
        if (e.PropertyName == nameof(AppModel.WindowSessions)) RestoreSession();
        if (e.PropertyName == nameof(AppModel.IsFlow) && !model.IsFlow) Sessions.Focus();
    }

    // ---- Sidebar

    void RestoreSidebar()
    {
        restoring = true;
        SidebarList.SelectedItem = model.Sidebar.FirstOrDefault(s => s.Id == model.SidebarSelection) ?? model.Sidebar.FirstOrDefault();
        restoring = false;
    }

    void SidebarChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!restoring && SidebarList.SelectedItem is SidebarItem s) model.SidebarSelection = s.Id;
    }

    static ProfileItem? ItemOf(object sender) =>
        ((sender as FrameworkElement)?.DataContext as SidebarItem)?.Profile;

    void SidebarMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = ItemOf(sender);
        if (item == null) { e.Handled = true; return; }
        var menu = ((FrameworkElement)sender).ContextMenu;
        foreach (var m in menu.Items.OfType<MenuItem>())
        {
            m.Visibility = (string)m.Header switch
            {
                "New Terminal" or "Open VS Code" => item.IsUsable ? Visibility.Visible : Visibility.Collapsed,
                "Make default" => item.ShowMakeDefault ? Visibility.Visible : Visibility.Collapsed,
                "Finish sign-in" => item.IsPending ? Visibility.Visible : Visibility.Collapsed,
                "Sign in" => item.IsLoggedOut ? Visibility.Visible : Visibility.Collapsed,
                _ => Visibility.Visible,
            };
            if ((string)m.Header is "New Terminal" or "Sign in") m.IsEnabled = item.ShellInstalled;
        }
    }

    static ProfileItem? MenuTarget(object sender) =>
        ((sender as MenuItem)?.DataContext as SidebarItem)?.Profile;

    void SidebarNewTerminal(object sender, RoutedEventArgs e) => MenuTarget(sender)?.NewTerminal.Execute(null);
    void SidebarVSCode(object sender, RoutedEventArgs e) => MenuTarget(sender)?.OpenVSCode.Execute(null);
    void SidebarMakeDefault(object sender, RoutedEventArgs e) => MenuTarget(sender)?.MakeDefault.Execute(null);
    void SidebarFinish(object sender, RoutedEventArgs e) => MenuTarget(sender)?.FinishSignIn.Execute(null);
    void SidebarSignIn(object sender, RoutedEventArgs e) => MenuTarget(sender)?.SignIn.Execute(null);
    void SidebarSettings(object sender, RoutedEventArgs e) => MenuTarget(sender)?.Settings.Execute(null);

    // ---- Sessions

    string? selectedSession;

    SessionItem? Selected => Sessions.SelectedItem as SessionItem;

    void RestoreSession()
    {
        if (selectedSession == null) return;
        var again = model.WindowSessions.FirstOrDefault(s => s.Row.SessionId == selectedSession);
        if (again == null) return;
        Sessions.SelectedItem = again;
    }

    void SessionsSelected(object sender, SelectionChangedEventArgs e)
    {
        // A swapped list clears the selection first; that is not the user choosing nothing.
        if (Selected is { } s) selectedSession = s.Row.SessionId;
    }

    void SessionsKey(object sender, KeyEventArgs e)
    {
        if (Selected is not { } s) return;
        if (e.Key == Key.Enter)
        {
            if (!s.IsLive && s.ShellInstalled) s.Resume.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (s.IsLive) s.Stop.Execute(null);
            e.Handled = true;
        }
    }

    void SessionsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Sessions, d) is DataGridRow &&
            Selected is { IsLive: false, ShellInstalled: true } s)
            s.Resume.Execute(null);
    }

    void SessionsMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Selected is not { } s) { e.Handled = true; return; }
        ResumeItem.Visibility = s.IsLive ? Visibility.Collapsed : Visibility.Visible;
        ResumeItem.IsEnabled = s.ShellInstalled;
        StopItem.Visibility = s.IsLive ? Visibility.Visible : Visibility.Collapsed;
    }

    void ResumeClick(object sender, RoutedEventArgs e) => Selected?.Resume.Execute(null);
    void StopClick(object sender, RoutedEventArgs e) => Selected?.Stop.Execute(null);

    void CopyFolder(object sender, RoutedEventArgs e)
    {
        if (Selected?.Row.Cwd is { } f) Clipboard.SetText(f);
    }

    void CopyId(object sender, RoutedEventArgs e)
    {
        if (Selected is { } s) Clipboard.SetText(s.Row.SessionId);
    }

    void OpenFolder(object sender, RoutedEventArgs e)
    {
        if (Selected?.Row.Cwd is { } f && Directory.Exists(f))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { f }, UseShellExecute = false })?.Dispose();
        else if (Selected != null) model.ErrorText = "That folder no longer exists.";
    }

    // ---- Toolbar

    void Go(Route r)
    {
        model.ErrorText = null;
        model.Route = r;
    }

    void Add(object sender, RoutedEventArgs e) => Go(new Route.Add());
    void Shell(object sender, RoutedEventArgs e) => Go(new Route.Shell());
    void Doctor(object sender, RoutedEventArgs e) => Go(new Route.Doctor());
    void RefreshClick(object sender, RoutedEventArgs e) => _ = model.Refresh();
    void NewTerminal(object sender, RoutedEventArgs e) => _ = model.NewSessionOnTarget();
}
