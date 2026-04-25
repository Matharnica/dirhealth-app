using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DirHealth.Desktop.ViewModels;

namespace DirHealth.Desktop.Views.Dashboard;

public partial class DashboardView : UserControl
{
    private DashboardViewModel?        _currentVm;
    private PropertyChangedEventHandler? _handler;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_currentVm != null && _handler != null)
                _currentVm.PropertyChanged -= _handler;

            _currentVm = DataContext as DashboardViewModel;
            if (_currentVm != null)
            {
                _handler = (_, e) =>
                {
                    if (e.PropertyName == nameof(DashboardViewModel.ScoreHistory))
                        DrawChart();
                };
                _currentVm.PropertyChanged += _handler;
            }
        };
    }

    private void TrendChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        TrendChart.Children.Clear();
        if (DataContext is not DashboardViewModel vm) return;
        var history = vm.ScoreHistory;
        if (history.Count < 2) return;

        double w = TrendChart.ActualWidth;
        double h = TrendChart.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var points = new PointCollection();
        for (int i = 0; i < history.Count; i++)
        {
            double x = i * (w / (history.Count - 1));
            double y = h - (history[i].Score / 100.0 * h);
            points.Add(new Point(x, y));
        }

        var poly = new Polygon
        {
            Fill   = new SolidColorBrush(Color.FromArgb(30, 99, 102, 241)),
            Points = new PointCollection(points)
        };
        poly.Points.Insert(0, new Point(0, h));
        poly.Points.Add(new Point(w, h));
        TrendChart.Children.Add(poly);

        var line = new Polyline
        {
            Stroke          = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
            StrokeThickness = 2,
            Points          = points,
            StrokeLineJoin  = PenLineJoin.Round,
        };
        TrendChart.Children.Add(line);

        foreach (var pt in points)
        {
            var dot = new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(99, 102, 241)) };
            Canvas.SetLeft(dot, pt.X - 2);
            Canvas.SetTop(dot, pt.Y - 2);
            TrendChart.Children.Add(dot);
        }
    }
}
