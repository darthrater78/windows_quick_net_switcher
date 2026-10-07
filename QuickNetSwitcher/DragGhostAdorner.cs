#nullable enable
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickNetSwitcher;

// Drag feedback for the adapter list.
//
// WPF supplies nothing beyond the drag cursor, so reordering showed only a small box
// and gave no sense of which row was moving. This paints a translucent copy of the
// whole row instead, pinned to the list's left edge so it slides vertically the way
// the reorder itself does.
public sealed class DragGhostAdorner : Adorner
{
    private readonly Brush _ghost;
    private readonly Size _size;
    private readonly double _grabOffset;
    private double _top;
    private double? _insertAt;

    /// <param name="row">The row being dragged; painted live through a VisualBrush.</param>
    /// <param name="grabOffset">Where inside the row the pointer took hold, so the ghost
    /// does not jump to have its top edge under the cursor.</param>
    public DragGhostAdorner(UIElement adornedElement, UIElement row, double grabOffset)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
        _size = row.RenderSize;
        _grabOffset = grabOffset;
        _ghost = new VisualBrush(row) { Opacity = 0.6 };
    }

    /// <param name="insertAt">Where the row would land if dropped now, as a Y offset in
    /// the list, or null when the pointer is not over another row.</param>
    public void UpdatePosition(Point position, double? insertAt = null)
    {
        _top = position.Y - _grabOffset;
        _insertAt = insertAt;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_size.Width <= 0 || _size.Height <= 0) return;

        drawingContext.DrawRectangle(_ghost, null, new Rect(new Point(0, _top), _size));

        // A 2px bar on the edge the row will take up, so the drop is not a guess.
        if (_insertAt is { } y && TryFindResource("AccentBrush") is Brush accent)
            drawingContext.DrawRectangle(accent, null, new Rect(0, y - 1, _size.Width, 2));
    }
}
