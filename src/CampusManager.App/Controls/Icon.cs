using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CampusManager.App.Controls;

/// <summary>Icône au trait (géométries 20×20 de Theme/Icons.xaml), couleur = Foreground hérité.</summary>
public class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata("files", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SpinProperty = DependencyProperty.Register(nameof(Spin), typeof(bool), typeof(Icon),
        new PropertyMetadata(false, (d, _) => ((Icon)d).UpdateSpin()));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public bool Spin { get => (bool)GetValue(SpinProperty); set => SetValue(SpinProperty, value); }

    public Icon() { SnapsToDevicePixels = false; RenderTransformOrigin = new Point(0.5, 0.5); }

    protected override Size MeasureOverride(Size _) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (TryFindResource("I." + Kind) is not Geometry g) return;
        var pen = new Pen(Foreground, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new ScaleTransform(Size / 20, Size / 20));
        dc.DrawGeometry(null, pen, g);
        dc.Pop();
    }

    void UpdateSpin()
    {
        if (Spin)
        {
            var r = new RotateTransform();
            RenderTransform = r;
            r.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else RenderTransform = Transform.Identity;
    }
}
