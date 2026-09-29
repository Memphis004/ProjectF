using UnityEngine;
using TMPro;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>TextAnchor (legacy uGUI) -> TextAlignmentOptions (TMP).
    /// TMP's flags layout differs from TextAnchor's 0..8 ordering, so a real
    /// map — not an int cast — is required (int cast compiles and silently
    /// produces wrong alignment).</summary>
    public static class TextAnchorMapper
    {
        public static TextAlignmentOptions Map(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.TopLeft,
        };
    }
}
