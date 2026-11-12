using System.Windows;
using System.Windows.Controls;

namespace GhstRDP.Ui;

/// <summary>
/// Wrap panel that fits as many columns of at least <see cref="MinItemWidth"/> as possible
/// and stretches them to fill the full width.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    (int Columns, double ItemWidth) Layout(double width)
    {
        var count = Math.Max(1, InternalChildren.Count);
        if (double.IsInfinity(width) || width <= 0) width = MinItemWidth * Math.Min(count, 4) + Spacing * (Math.Min(count, 4) - 1);
        var columns = (int)Math.Floor((width + Spacing) / (MinItemWidth + Spacing));
        columns = Math.Clamp(columns, 1, Math.Max(1, MaxColumns));
        var itemWidth = Math.Max(0, (width - Spacing * (columns - 1)) / columns);
        return (columns, Math.Floor(itemWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = Layout(availableSize.Width);
        double total = 0, rowHeight = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if ((i + 1) % columns == 0 || i == InternalChildren.Count - 1)
            {
                total += rowHeight + (i == InternalChildren.Count - 1 ? 0 : Spacing);
                rowHeight = 0;
            }
        }
        var width = double.IsInfinity(availableSize.Width) ? columns * itemWidth + (columns - 1) * Spacing : availableSize.Width;
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width);
        double y = 0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var end = Math.Min(start + columns, InternalChildren.Count);
            double rowHeight = 0;
            for (var i = start; i < end; i++) rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
            for (var i = start; i < end; i++)
            {
                var x = (i - start) * (itemWidth + Spacing);
                InternalChildren[i].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
