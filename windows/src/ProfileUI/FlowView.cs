using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ProfileUI;

/// Shows whichever step the model's route names. Used by the panel, and over the window.
public sealed class FlowView : ContentControl
{
    AppModel? model;
    Route? shown;

    public FlowView()
    {
        Padding = new Thickness(16);
        DataContextChanged += (_, _) => Attach(DataContext as AppModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as AppModel);
        // The panel and the window both hold a FlowView. Only a visible one builds the step, so work a
        // step starts when it opens (Doctor, the sign-in check) runs once.
        IsVisibleChanged += (_, _) => Show();
    }

    void Attach(AppModel? m)
    {
        if (model != null) model.PropertyChanged -= Changed;
        model = m;
        if (model != null) model.PropertyChanged += Changed;
        Show();
    }

    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.Route)) Show();
    }

    void Show()
    {
        // A hidden host keeps what it shows, so text typed in a step survives the panel closing.
        if (!IsVisible) return;
        var route = model?.Route;
        if (route == shown) return;
        shown = route;
        Content = route switch
        {
            Route.Add => new AddProfileView(),
            Route.Waiting w => new WaitingView(w.ProfileId),
            Route.Settings s => new SettingsView(s.ProfileId),
            Route.Doctor => new DoctorView(),
            Route.Shell => new ShellSetupView(),
            Route.Stop s => new StopView(s.SessionId),
            _ => null,
        };
        if (Content is FrameworkElement f)
        {
            f.DataContext = model;
            f.Loaded += (_, _) => f.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        }
    }
}

/// The colour swatches of the add and settings steps.
public sealed class Swatches : StackPanel
{
    public event Action<string>? Picked;
    string selected = AppModel.ProfileColors[0];

    public Swatches() => Orientation = Orientation.Horizontal;

    public string Selected
    {
        get => selected;
        set { selected = value; Build(); }
    }

    public IReadOnlyList<string> Colors { get; set; } = AppModel.ProfileColors;

    public void Build()
    {
        Children.Clear();
        var list = Colors.Contains(selected) ? Colors : [selected, .. Colors];
        foreach (var c in list)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 22, Height = 22, Fill = HexToBrush.Brush(c) };
            var ring = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = dot, Padding = new Thickness(2),
                BorderThickness = new Thickness(c == selected ? 2 : 0),
            };
            ring.SetResourceReference(Border.BorderBrushProperty, "TextFillColorPrimaryBrush");
            var b = new Button { Content = ring, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), Background = null, BorderThickness = new Thickness(0) };
            System.Windows.Automation.AutomationProperties.SetName(b, $"Colour {c}");
            var colour = c;
            b.Click += (_, _) => { Selected = colour; Picked?.Invoke(colour); };
            Children.Add(b);
        }
    }
}
