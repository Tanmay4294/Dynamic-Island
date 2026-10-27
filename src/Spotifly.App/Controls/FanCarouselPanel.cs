using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Spotifly.App.Controls;

/// <summary>
/// Arranges playlist cards on a shallow fan.  The offset is animated by the
/// owner; arranging from that animated value gives every card a smooth shared
/// movement while keeping the active card centered and the edges progressively
/// rotated and reduced in size.
/// </summary>
public sealed class FanCarouselPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(FanCarouselPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange, null, CoerceOffset));

    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(FanCarouselPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public static readonly DependencyProperty CardHeightProperty = DependencyProperty.Register(
        nameof(CardHeight), typeof(double), typeof(FanCarouselPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double CardHeight
    {
        get => (double)GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public int ItemCount => InternalChildren.Count;

    public void SetOffset(double value)
    {
        BeginAnimation(OffsetProperty, null);
        Offset = (double)CoerceOffset(this, value);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 320 : Math.Max(0, availableSize.Width);
        double height = double.IsInfinity(availableSize.Height) ? 108 : Math.Max(0, availableSize.Height);
        var cardSize = GetCardSize(width, height);
        foreach (UIElement child in InternalChildren)
            child.Measure(cardSize);
        return new System.Windows.Size(width, height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        int count = InternalChildren.Count;
        if (count == 0)
            return finalSize;

        double width = Math.Max(1, finalSize.Width);
        double height = Math.Max(1, finalSize.Height);
        var cardSize = GetCardSize(width, height);
        double centerX = width / 2;
        double centerOffset = PositiveModulo(Offset, count);
        double baseY = Math.Max(0, (height - cardSize.Height) * 0.08);

        for (int i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            double relative = i - centerOffset;
            if (relative > count / 2.0)
                relative -= count;
            else if (relative < -count / 2.0)
                relative += count;
            double distance = Math.Abs(relative);
            if (distance > 2.45)
            {
                child.Visibility = Visibility.Hidden;
                child.IsHitTestVisible = false;
                continue;
            }

            child.Visibility = Visibility.Visible;
            child.IsHitTestVisible = true;
            double normalized = Math.Clamp(relative / 2.2, -1, 1);
            double angle = normalized * 39;
            double radians = Math.Abs(angle) * Math.PI / 180;
            double scale = Math.Max(0.68, 1 - distance * 0.14);
            double spacing = Math.Min(cardSize.Width * 0.94, width * 0.29);
            double x = centerX + relative * spacing - cardSize.Width / 2;
            double y = baseY + (1 - Math.Cos(radians)) * Math.Min(42, height * 0.38);

            child.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            child.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection
                {
                    new TranslateTransform(),
                    new RotateTransform(angle),
                    new ScaleTransform(scale, scale)
                }
            };
            child.Opacity = Math.Max(0.62, 1 - distance * 0.13);
            SetZIndex(child, 100 - (int)Math.Round(distance * 10));
            child.Arrange(new Rect(x, y, cardSize.Width, cardSize.Height));
        }

        return finalSize;
    }

    private System.Windows.Size GetCardSize(double width, double height) => new(
        CardWidth > 0 ? CardWidth : Math.Clamp(width * 0.34, 92, 116),
        CardHeight > 0 ? CardHeight : Math.Clamp(height * 0.90, 92, 100));

    private static object CoerceOffset(DependencyObject d, object baseValue)
    {
        var panel = (FanCarouselPanel)d;
        double value = baseValue is double number && double.IsFinite(number) ? number : 0;
        return value;
    }

    private static double PositiveModulo(double value, int modulus)
    {
        double result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
