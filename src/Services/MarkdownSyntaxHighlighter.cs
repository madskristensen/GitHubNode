using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Services
{
    /// <summary>
    /// Provides syntax highlighting for markdown content in FlowDocument format.
    /// Supports YAML front matter, code blocks, headers, links, and inline code.
    /// </summary>
    internal static class MarkdownSyntaxHighlighter
    {
        private static readonly SolidColorBrush _headerBrush = SyntaxColors.CreateBrush(SyntaxColors.Header);
        private static readonly SolidColorBrush _commentBrush = SyntaxColors.CreateBrush(SyntaxColors.Comment);
        private static readonly SolidColorBrush _stringBrush = SyntaxColors.CreateBrush(SyntaxColors.String);
        private static readonly SolidColorBrush _keywordBrush = SyntaxColors.CreateBrush(SyntaxColors.Keyword);
        private static readonly SolidColorBrush _codeBrush = SyntaxColors.CreateBrush(SyntaxColors.Code);

        /// <summary>
        /// Creates a FlowDocument with syntax highlighting for the given markdown content.
        /// </summary>
        /// <param name="content">The markdown content to highlight.</param>
        /// <param name="showTruncated">Whether to show a truncation indicator at the end.</param>
        /// <returns>A FlowDocument with syntax-highlighted content.</returns>
        public static FlowDocument CreateHighlightedDocument(string content, bool showTruncated = false)
        {
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                PagePadding = new Thickness(0)
            };
            document.SetResourceReference(FlowDocument.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);

            var lines = content.Split('\n');
            var inCodeBlock = false;
            var inYamlFrontMatter = false;
            var yamlDashCount = 0;

            foreach (var line in lines)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 1 };
                var trimmedLine = line.TrimEnd('\r');

                // Track YAML front matter (between --- markers)
                if (trimmedLine == "---")
                {
                    yamlDashCount++;
                    inYamlFrontMatter = yamlDashCount == 1;
                    paragraph.Inlines.Add(new Run(trimmedLine) { Foreground = _commentBrush });
                }
                // Track code blocks
                else if (trimmedLine.StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    paragraph.Inlines.Add(new Run(trimmedLine) { Foreground = _commentBrush });
                }
                else if (inCodeBlock)
                {
                    // Code block content - use default color
                    paragraph.Inlines.Add(new Run(trimmedLine));
                }
                else if (inYamlFrontMatter)
                {
                    // YAML front matter - highlight keys
                    HighlightYamlLine(paragraph, trimmedLine);
                }
                // Markdown headers
                else if (Regex.IsMatch(trimmedLine, @"^#{1,6}\s"))
                {
                    paragraph.Inlines.Add(new Run(trimmedLine) { Foreground = _headerBrush, FontWeight = FontWeights.Bold });
                }
                // HTML/XML comments
                else if (trimmedLine.TrimStart().StartsWith("<!--") || trimmedLine.TrimStart().StartsWith("-->") ||
                         (trimmedLine.Contains("<!--") && trimmedLine.Contains("-->")))
                {
                    paragraph.Inlines.Add(new Run(trimmedLine) { Foreground = _commentBrush });
                }
                // Lines with inline code or links
                else if (trimmedLine.Contains("`") || trimmedLine.Contains("["))
                {
                    HighlightInlineElements(paragraph, trimmedLine);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(trimmedLine));
                }

                document.Blocks.Add(paragraph);
            }

            if (showTruncated)
            {
                var truncatedPara = new Paragraph(new Run("\n... (truncated)") { Foreground = _commentBrush })
                {
                    Margin = new Thickness(0)
                };
                document.Blocks.Add(truncatedPara);
            }

            return document;
        }

        private static void HighlightYamlLine(Paragraph paragraph, string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0 && !line.TrimStart().StartsWith("-"))
            {
                var key = line.Substring(0, colonIndex + 1);
                var value = line.Substring(colonIndex + 1);
                paragraph.Inlines.Add(new Run(key) { Foreground = _keywordBrush });
                if (!string.IsNullOrWhiteSpace(value))
                {
                    paragraph.Inlines.Add(new Run(value) { Foreground = _stringBrush });
                }
            }
            else
            {
                paragraph.Inlines.Add(new Run(line));
            }
        }

        private static void HighlightInlineElements(Paragraph paragraph, string line)
        {
            // Simple highlighting for inline code (`code`) and links [text](url)
            var i = 0;
            while (i < line.Length)
            {
                // Check for inline code
                if (line[i] == '`')
                {
                    var endIndex = line.IndexOf('`', i + 1);
                    if (endIndex > i)
                    {
                        paragraph.Inlines.Add(new Run(line.Substring(i, endIndex - i + 1)) { Foreground = _codeBrush });
                        i = endIndex + 1;
                        continue;
                    }
                }
                // Check for markdown links [text](url)
                else if (line[i] == '[')
                {
                    Match match = Regex.Match(line.Substring(i), @"^\[([^\]]+)\]\(([^)]+)\)");
                    if (match.Success)
                    {
                        paragraph.Inlines.Add(new Run("["));
                        paragraph.Inlines.Add(new Run(match.Groups[1].Value) { Foreground = _stringBrush });
                        paragraph.Inlines.Add(new Run("]("));
                        paragraph.Inlines.Add(new Run(match.Groups[2].Value) { Foreground = _stringBrush, TextDecorations = TextDecorations.Underline });
                        paragraph.Inlines.Add(new Run(")"));
                        i += match.Length;
                        continue;
                    }
                }

                // Find next special character or end of line
                var nextSpecial = line.Length;
                var nextBacktick = line.IndexOf('`', i + 1);
                var nextBracket = line.IndexOf('[', i + 1);
                if (nextBacktick >= 0 && nextBacktick < nextSpecial) nextSpecial = nextBacktick;
                if (nextBracket >= 0 && nextBracket < nextSpecial) nextSpecial = nextBracket;

                if (i < nextSpecial)
                {
                    paragraph.Inlines.Add(new Run(line.Substring(i, nextSpecial - i)));
                    i = nextSpecial;
                }
                else
                {
                    paragraph.Inlines.Add(new Run(line[i].ToString()));
                    i++;
                }
            }
        }
    }
}
