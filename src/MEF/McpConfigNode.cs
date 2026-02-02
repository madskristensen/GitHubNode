using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GitHubNode.Services;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Represents an MCP configuration file in Solution Explorer.
    /// Shows the location name and contains server entries as children.
    /// </summary>
    internal sealed class McpConfigNode :
        McpNodeBase,
        IAttachedCollectionSource,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IInvocationPattern
    {
        private readonly McpConfigLocation _location;
        private readonly ObservableCollection<object> _children;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IInvocationPattern),
            typeof(ISupportDisposalNotification),
        ];

        public McpConfigNode(McpConfigLocation location, object parent)
            : base(parent, parent)
        {
            _location = location;
            _children = [];

            RefreshChildren();
        }

        /// <summary>
        /// Gets the configuration location information.
        /// </summary>
        public McpConfigLocation Location => _location;

        /// <summary>
        /// Gets the file path to this configuration.
        /// </summary>
        public string FilePath => _location.FilePath;

        // IAttachedCollectionSource
        public bool HasItems => _children.Count > 0;
        public IEnumerable Items => _children;

        /// <summary>
        /// Gets the children for search enumeration without modifying the tree.
        /// </summary>
        public IEnumerable<McpNodeBase> GetChildrenForSearch()
        {
            return _children.OfType<McpNodeBase>();
        }

        // ITreeDisplayItem
        public override string Text => _location.DisplayName;
        public override string ToolTipText => $"{_location.FilePath}\n\n{_location.Description}";

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => KnownMonikers.Application;
        public ImageMoniker ExpandedIconMoniker => KnownMonikers.Application;
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => _location.IsSourceControlled ? KnownMonikers.SourceControl : default;

        // IPrioritizedComparable
        public int Priority => 0;

        public int CompareTo(object obj)
        {
            return obj is ITreeDisplayItem other ? StringComparer.OrdinalIgnoreCase.Compare(Text, other.Text) : 0;
        }

        // IInvocationPattern - double-click opens the config file
        public IInvocationController InvocationController => McpInvocationController.Instance;
        public bool CanPreview => true;

        /// <summary>
        /// Refreshes the server entries from the configuration file.
        /// </summary>
        public void RefreshChildren()
        {
            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }
            _children.Clear();

            // Re-parse the file to get current server names and their transport types
            Dictionary<string, string> serverInfo = McpConfigService.ParseServerInfo(_location.FilePath);

            foreach (KeyValuePair<string, string> kvp in serverInfo)
            {
                _children.Add(new McpServerNode(kvp.Key, _location.FilePath, kvp.Value, this));
            }

            RaisePropertyChanged(nameof(HasItems));
            RaisePropertyChanged(nameof(Items));
        }

        protected override void OnDisposing()
        {
            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }
            _children.Clear();
        }
    }
}
