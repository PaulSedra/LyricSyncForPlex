using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LyricSyncForPlex.Controls;

public sealed class InteractiveCursorGrid : Grid
{
    private static readonly InputSystemCursor HandCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    public static readonly DependencyProperty IsClickableProperty = DependencyProperty.RegisterAttached(
        "IsClickable",
        typeof(bool),
        typeof(InteractiveCursorGrid),
        new PropertyMetadata(false));

    public InteractiveCursorGrid()
    {
        PointerEntered += UpdateCursor;
        PointerMoved += UpdateCursor;
    }

    /// <summary>
    /// Gets whether an element should use the hand cursor when the pointer is over it.
    /// </summary>
    /// <param name="element">The element to inspect.</param>
    /// <returns><see langword="true"/> when the element is marked clickable.</returns>
    public static bool GetIsClickable(DependencyObject element)
    {
        return (bool)element.GetValue(IsClickableProperty);
    }

    /// <summary>
    /// Marks an element to use the hand cursor when the pointer is over it.
    /// </summary>
    /// <param name="element">The element to mark.</param>
    /// <param name="value">Whether the element is clickable.</param>
    public static void SetIsClickable(DependencyObject element, bool value)
    {
        element.SetValue(IsClickableProperty, value);
    }

    /// <summary>
    /// Uses a hand cursor over interactive controls and marked pointer targets.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The pointer event data.</param>
    private void UpdateCursor(object sender, PointerRoutedEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null && !ReferenceEquals(current, this))
        {
            if (current is ButtonBase or SelectorItem or Slider or ToggleSwitch || GetIsClickable(current))
            {
                ProtectedCursor = HandCursor;
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        ProtectedCursor = null;
    }
}
