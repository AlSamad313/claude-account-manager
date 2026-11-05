using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ProfileUI;

/// A flow step: calls Update whenever the model changes while it is on screen.
public class FlowBase : UserControl
{
    AppModel? attached;

    public FlowBase()
    {
        Loaded += (_, _) => { Attach(DataContext as AppModel); Update(); };
        Unloaded += (_, _) => Attach(null);
        DataContextChanged += (_, _) => { if (IsLoaded) { Attach(DataContext as AppModel); Update(); } };
    }

    protected AppModel Model => (AppModel)DataContext;

    void Attach(AppModel? m)
    {
        if (attached != null) attached.PropertyChanged -= Changed;
        attached = m;
        if (attached != null) attached.PropertyChanged += Changed;
    }

    void Changed(object? sender, PropertyChangedEventArgs e) => Update();

    protected virtual void Update() { }

    protected void Home(object sender, RoutedEventArgs e)
    {
        Model.ErrorText = null;
        Model.Route = Route.Home;
    }
}
