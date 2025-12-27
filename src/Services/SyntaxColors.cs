using System.Windows.Media;

namespace GitHubNode.Services
{
    /// <summary>
    /// Defines colors for syntax highlighting in markdown preview.
    /// Colors are based on VS dark theme for consistency.
    /// </summary>
    internal static class SyntaxColors
    {
        /// <summary>Blue color for markdown headers.</summary>
        public static readonly Color Header = Color.FromRgb(86, 156, 214);

        /// <summary>Green color for comments (HTML, YAML markers).</summary>
        public static readonly Color Comment = Color.FromRgb(106, 153, 85);

        /// <summary>Orange color for strings, URLs, and YAML values.</summary>
        public static readonly Color String = Color.FromRgb(206, 145, 120);

        /// <summary>Purple color for YAML keys and keywords.</summary>
        public static readonly Color Keyword = Color.FromRgb(197, 134, 192);

        /// <summary>Light blue color for inline code.</summary>
        public static readonly Color Code = Color.FromRgb(156, 220, 254);

        /// <summary>Creates a frozen SolidColorBrush for the specified color.</summary>
        public static SolidColorBrush CreateBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
