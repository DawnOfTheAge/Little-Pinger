using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LittlePinger;

/// <summary>
/// A self-contained ping-latency graph. Bind <see cref="SamplesSource"/> to a
/// <see cref="ObservableCollection{T}"/> of <c>double?</c> latency values; the control
/// redraws automatically whenever the collection changes or the control is resized.
/// <c>null</c> samples are rendered as red timeout dots.
/// </summary>
public partial class PingGraphControl : UserControl
{
    public static readonly DependencyProperty SamplesSourceProperty =
        DependencyProperty.Register(
            nameof(SamplesSource),
            typeof(ObservableCollection<double?>),
            typeof(PingGraphControl),
            new PropertyMetadata(null, OnSamplesSourceChanged));

    public ObservableCollection<double?> SamplesSource
    {
        get => (ObservableCollection<double?>)GetValue(SamplesSourceProperty);
        set => SetValue(SamplesSourceProperty, value);
    }

    private static void OnSamplesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (PingGraphControl)d;
        if (e.OldValue is ObservableCollection<double?> old)
            old.CollectionChanged -= ctrl.OnSamplesChanged;
        if (e.NewValue is ObservableCollection<double?> @new)
            @new.CollectionChanged += ctrl.OnSamplesChanged;
        ctrl.Redraw();
    }

    private void OnSamplesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    public PingGraphControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    private void Redraw()
    {
        double w = GraphCanvas.ActualWidth, h = GraphCanvas.ActualHeight;
        if (w < 2 || h < 2) { GraphLine.Points = new PointCollection(); return; }

        var samples = SamplesSource?.ToList() ?? new List<double?>();

        // Remove previous timeout dots, keep the named Polyline
        for (int i = GraphCanvas.Children.Count - 1; i >= 0; i--)
            if (GraphCanvas.Children[i] is Ellipse) GraphCanvas.Children.RemoveAt(i);

        if (samples.Count < 2) { GraphLine.Points = new PointCollection(); return; }

        var valid = samples.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        double max = valid.Count > 0 ? Math.Max(valid.Max() * 1.15, 1) : 100;
        int    n   = samples.Count;
        var    pts = new PointCollection();

        for (int i = 0; i < n; i++)
        {
            double x = w * i / (n - 1);
            if (samples[i].HasValue)
            {
                double y = h - samples[i]!.Value / max * (h - 4) - 2;
                pts.Add(new Point(x, Clamp(y, 0, h)));
            }
            else
            {
                var dot = new Ellipse { Width = 5, Height = 5, Fill = Brushes.Red };
                Canvas.SetLeft(dot, x - 2.5);
                Canvas.SetTop(dot, h / 2 - 2.5);
                GraphCanvas.Children.Add(dot);
            }
        }
        GraphLine.Points = pts;
    }

    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;
}
