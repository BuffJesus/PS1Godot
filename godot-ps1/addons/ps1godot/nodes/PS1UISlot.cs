using Godot;

namespace PS1Godot;

// Per-axis alignment of a child inside its container's available space.
// "Inherit" defers to the container's default H/VAlign — keeps simple cases
// uncluttered. "Fill" makes the child stretch to occupy the cross-axis (or
// main axis when SlotFlex > 0); the other three pin the child to a side.
//
// The interpretation depends on which container the widget lives in:
//   - Inside PS1UIHBox: HAlign is ignored on the main (horizontal) axis when
//     SlotFlex > 0 (child is stretched). VAlign is the cross-axis pin.
//   - Inside PS1UIVBox: vice-versa.
//   - Inside PS1UISizeBox / PS1UIOverlay: both axes pin the child within the
//     container's rect.
//   - Inside PS1UICanvas (root, anchor-based): slot fields are ignored — use
//     the element's own Anchor + X/Y instead.
public enum PS1UISlotAlign
{
    Inherit = 0,
    Start = 1,    // Left (HAlign) / Top (VAlign)
    Center = 2,
    End = 3,      // Right (HAlign) / Bottom (VAlign)
    Fill = 4,     // Stretch to fill axis
}
