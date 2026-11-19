using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace LyricSyncForPlex.Controls;

/// <summary>
/// Stacks lyrics around the middle lyric block, allowing each lyric to wrap independently.
/// </summary>
public sealed class CenteredLyricsPanel : Panel
{
    private const double LineSpacing = 2;

    /// <summary>
    /// Measures each lyric at the available width with room for all of its wrapped rows.
    /// </summary>
    /// <param name="availableSize">The size available in the lyrics viewport.</param>
    /// <returns>The desired stack size within the viewport.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        Size lineSize = new(availableSize.Width, double.PositiveInfinity);
        double width = 0;
        double height = LineSpacing * Math.Max(0, Children.Count - 1);
        foreach (UIElement line in Children)
        {
            line.Measure(lineSize);
            width = Math.Max(width, line.DesiredSize.Width);
            height += line.DesiredSize.Height;
        }

        return new Size(width, Math.Min(height, availableSize.Height));
    }

    /// <summary>
    /// Centers the middle lyric block and lays out its neighbors at their measured heights.
    /// </summary>
    /// <param name="finalSize">The arranged size of the lyrics viewport.</param>
    /// <returns>The size used by the panel.</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
            return finalSize;

        int middle = Children.Count / 2;
        double top = (finalSize.Height - Children[middle].DesiredSize.Height) / 2;
        for (int index = 0; index < middle; index++)
            top -= Children[index].DesiredSize.Height + LineSpacing;

        foreach (UIElement line in Children)
        {
            double height = line.DesiredSize.Height;
            line.Arrange(new Rect(0, top, finalSize.Width, height));
            top += height + LineSpacing;
        }

        return finalSize;
    }
}
