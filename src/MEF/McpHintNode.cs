using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// A hint node shown when no MCP configurations exist.
    /// Displays "(No configurations found)" in italic.
    /// </summary>
    internal sealed class McpHintNode :
        McpNodeBase,
        ITreeDisplayItemWithImages
    {
        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(ISupportDisposalNotification),
        ];

        public McpHintNode(object parent)
            : base(parent)
        {
        }

        // ITreeDisplayItem
        public override string Text => "(No configurations found)";
        public override string ToolTipText => "Right-click the MCP Servers node to add a configuration";
        public override FontStyle FontStyle => FontStyles.Italic;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => KnownMonikers.StatusInformation;
        public ImageMoniker ExpandedIconMoniker => KnownMonikers.StatusInformation;
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;
    }
}
