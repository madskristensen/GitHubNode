using System;
using System.Collections.Generic;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Represents an MCP server entry within a configuration file.
    /// Clicking opens the parent configuration file for editing.
    /// </summary>
    internal sealed class McpServerNode :
        McpNodeBase,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IInvocationPattern
    {
        private readonly string _serverName;
        private readonly string _configFilePath;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IInvocationPattern),
            typeof(ISupportDisposalNotification),
        ];

        public McpServerNode(string serverName, string configFilePath, object parent)
            : base(parent)
        {
            _serverName = serverName;
            _configFilePath = configFilePath;
        }

        /// <summary>
        /// Gets the server name.
        /// </summary>
        public string ServerName => _serverName;

        /// <summary>
        /// Gets the path to the parent configuration file.
        /// </summary>
        public string ConfigFilePath => _configFilePath;

        // ITreeDisplayItem
        public override string Text => _serverName;
        public override string ToolTipText => $"Server: {_serverName}\nClick to open configuration file";

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => KnownMonikers.WebService;
        public ImageMoniker ExpandedIconMoniker => KnownMonikers.WebService;
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        // IPrioritizedComparable
        public int Priority => 1;

        public int CompareTo(object obj)
        {
            return obj is ITreeDisplayItem other ? StringComparer.OrdinalIgnoreCase.Compare(Text, other.Text) : 0;
        }

        // IInvocationPattern - double-click opens the config file
        public IInvocationController InvocationController => McpInvocationController.Instance;
        public bool CanPreview => true;
    }
}
