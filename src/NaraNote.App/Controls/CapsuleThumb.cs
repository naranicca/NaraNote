using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NaraNote.App.Controls;

public sealed class CapsuleThumb : Thumb
{
    private System.Windows.Controls.Primitives.ScrollBar? _ownerScrollBar;

    public CapsuleThumb()
    {
        DragStarted += (_, _) => InvalidateVisual();
        DragCompleted += (_, _) => InvalidateVisual();
        Loaded += (_, _) => AttachOwnerScrollBar();
        Unloaded += (_, _) => DetachOwnerScrollBar();
    }
    public static readonly DependencyProperty MinimumVisualLengthProperty = DependencyProperty.Register(
        nameof(MinimumVisualLength), typeof(double), typeof(CapsuleThumb),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.Register(
        nameof(HoverBackground), typeof(System.Windows.Media.Brush), typeof(CapsuleThumb),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DragBackgroundProperty = DependencyProperty.Register(
        nameof(DragBackground), typeof(System.Windows.Media.Brush), typeof(CapsuleThumb),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double MinimumVisualLength
    {
        get => (double)GetValue(MinimumVisualLengthProperty);
        set => SetValue(MinimumVisualLengthProperty, value);
    }
    public System.Windows.Media.Brush? HoverBackground
    {
        get => (System.Windows.Media.Brush?)GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }
    public System.Windows.Media.Brush? DragBackground
    {
        get => (System.Windows.Media.Brush?)GetValue(DragBackgroundProperty);
        set => SetValue(DragBackgroundProperty, value);
    }

    protected override Geometry? GetLayoutClip(System.Windows.Size layoutSlotSize) => null;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var brush = IsDragging ? DragBackground ?? Background : IsMouseOver ? HoverBackground ?? Background : Background;
        var radius = Math.Min(ActualWidth, ActualHeight) / 2;
        drawingContext.DrawRoundedRectangle(brush, null, new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }
    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }
    private void AttachOwnerScrollBar()
    {
        DetachOwnerScrollBar();
        for (DependencyObject? current = VisualTreeHelper.GetParent(this); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not System.Windows.Controls.Primitives.ScrollBar scrollBar) continue;
            _ownerScrollBar = scrollBar;
            _ownerScrollBar.ValueChanged += OwnerScrollBar_ValueChanged;
            break;
        }
    }

    private void DetachOwnerScrollBar()
    {
        if (_ownerScrollBar is not null) _ownerScrollBar.ValueChanged -= OwnerScrollBar_ValueChanged;
        _ownerScrollBar = null;
    }

    private void OwnerScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => InvalidateVisual();
}
