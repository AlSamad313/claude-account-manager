using System.Windows;
using System.Windows.Controls;

namespace ProfileUI;

/// The tray panel. The app supplies what Open window and Exit do.
public partial class PanelView : UserControl
{
    public PanelView() => InitializeComponent();

    public Action? OpenWindowRequested { get; set; }
    public Action? ExitRequested { get; set; }

    AppModel Model => (AppModel)DataContext;

    void Go(Route r)
    {
        Model.ErrorText = null;
        Model.Route = r;
    }

    void Add(object sender, RoutedEventArgs e) => Go(new Route.Add());
    void Shell(object sender, RoutedEventArgs e) => Go(new Route.Shell());
    void Doctor(object sender, RoutedEventArgs e) => Go(new Route.Doctor());
    void CopySetup(object sender, RoutedEventArgs e) => Go(new Route.Copy());
    void Guide(object sender, RoutedEventArgs e) => Go(new Route.Guide());
    void OpenWindow(object sender, RoutedEventArgs e) => OpenWindowRequested?.Invoke();
    void Exit(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
