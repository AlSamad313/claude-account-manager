using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ProfileUI;

/// Errors and notices, shown at the top of the panel and the window.
public partial class MessageBanners : UserControl
{
    public MessageBanners() => InitializeComponent();

    AppModel Model => (AppModel)DataContext;

    void DismissError(object sender, RoutedEventArgs e) => Model.ErrorText = null;
    void DismissNotice(object sender, RoutedEventArgs e) => Model.NoticeText = null;
    void CancelFolder(object sender, RoutedEventArgs e) => Model.MissingFolderRow = null;

    void ChooseFolder(object sender, RoutedEventArgs e)
    {
        if (Model.MissingFolderRow is not { } row) return;
        var dialog = new OpenFolderDialog { Title = $"Choose a folder for \"{row.Title}\"" };
        var ok = UiState.WithModal(() => dialog.ShowDialog(Window.GetWindow(this)) == true);
        Model.MissingFolderRow = null;
        if (ok) _ = Model.Resume(row, dialog.FolderName);
    }
}
