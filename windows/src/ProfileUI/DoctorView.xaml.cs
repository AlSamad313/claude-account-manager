using System.Windows;
using System.Windows.Media;
using ProfileCore;

namespace ProfileUI;

public partial class DoctorView : FlowBase
{
    public sealed record Line(string Icon, Brush Brush, string Level, string Message, string? Fix);

    DoctorReport? shown;

    public DoctorView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = Model.RunDoctor();
    }

    protected override void Update()
    {
        Running.Visibility = Model.DoctorRunning ? Visibility.Visible : Visibility.Collapsed;
        RepairButton.Visibility = Model.DoctorReport?.HasFaults == true ? Visibility.Visible : Visibility.Collapsed;
        if (ReferenceEquals(shown, Model.DoctorReport)) return;
        shown = Model.DoctorReport;
        Findings.ItemsSource = shown?.Findings.Select(f => f.Level switch
        {
            FindingLevel.Ok => new Line("\uE73E", Res("SystemFillColorSuccessBrush", Brushes.Green), "ok", f.Message, f.Fix),
            FindingLevel.Note => new Line("\uE946", Res("TextFillColorSecondaryBrush", Brushes.Gray), "note", f.Message, f.Fix),
            _ => new Line("\uE7BA", Res("SystemFillColorCriticalBrush", Brushes.Red), "fault", f.Message, f.Fix is { } x ? "Fix: " + x : null),
        }).ToList();
    }

    Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    void Repair(object sender, RoutedEventArgs e) => _ = Model.FixAndRerunDoctor();
}
