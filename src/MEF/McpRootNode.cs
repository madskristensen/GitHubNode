using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using GitHubNode.Services;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// The root "MCP Servers" node shown as a child under the solution in Solution Explorer.
    /// Discovers and displays MCP configuration files from all supported locations.
    /// </summary>
    internal sealed class McpRootNode :
        McpNodeBase,
        IAttachedCollectionSource,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IContextMenuPattern
    {
        private readonly ObservableCollection<object> _children;
        private readonly string _solutionDirectory;
        private readonly List<FileSystemWatcher> _watchers;
        private CancellationTokenSource _debounceCts;
        private readonly object _debounceLock = new();

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public McpRootNode(object sourceItem, string solutionDirectory)
            : base(sourceItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _solutionDirectory = solutionDirectory;
            _children = [];
            _watchers = [];

            RefreshChildren();
            SetupFileWatchers();
        }

        /// <summary>
        /// Gets the solution directory path.
        /// </summary>
        public string SolutionDirectory => _solutionDirectory;

        // IAttachedCollectionSource
        public bool HasItems => _children.Count > 0;
        public IEnumerable Items => _children;

        // ITreeDisplayItem
        public override string Text => "MCP Servers";
        public override string ToolTipText => "Model Context Protocol server configurations";
        public override System.Windows.FontWeight FontWeight => System.Windows.FontWeights.SemiBold;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => CustomMonikers.McpIcon;
        public ImageMoniker ExpandedIconMoniker => CustomMonikers.McpIcon;
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        // IPrioritizedComparable - Priority 1 to appear after GitHub node (which is 0)
        public int Priority => 1;

        public int CompareTo(object obj)
        {
            if (obj is IPrioritizedComparable other)
            {
                return Priority.CompareTo(other.Priority);
            }
            return -1;
        }

        // IContextMenuPattern
        public IContextMenuController ContextMenuController => McpContextMenuController.Instance;

        /// <summary>
        /// Refreshes the children collection by re-discovering MCP configurations.
        /// </summary>
        public void RefreshChildren()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Dispose existing children
            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }
            _children.Clear();

            var existingLocations = McpConfigService.GetExistingLocations(_solutionDirectory);

            if (existingLocations.Count == 0)
            {
                // Show hint node when no configurations exist
                _children.Add(new McpHintNode(this));
            }
            else
            {
                // Add a node for each existing configuration file
                foreach (var location in existingLocations)
                {
                    _children.Add(new McpConfigNode(location, this));
                }
            }

            RaisePropertyChanged(nameof(HasItems));
            RaisePropertyChanged(nameof(Items));
        }

        private void SetupFileWatchers()
        {
            var locations = McpConfigService.GetAllLocations(_solutionDirectory);

            foreach (var location in locations)
            {
                var directory = Path.GetDirectoryName(location.FilePath);
                var fileName = Path.GetFileName(location.FilePath);

                if (string.IsNullOrEmpty(directory))
                {
                    continue;
                }

                // Create directory if it doesn't exist (for watching purposes, we need the parent)
                // But we can only watch directories that exist
                if (!Directory.Exists(directory))
                {
                    // Watch the parent directory instead if it exists
                    var parentDir = Path.GetDirectoryName(directory);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        try
                        {
                            var parentWatcher = new FileSystemWatcher(parentDir)
                            {
                                Filter = Path.GetFileName(directory),
                                NotifyFilter = NotifyFilters.DirectoryName,
                                IncludeSubdirectories = false
                            };
                            parentWatcher.Created += OnDirectoryCreated;
                            parentWatcher.EnableRaisingEvents = true;
                            _watchers.Add(parentWatcher);
                        }
                        catch
                        {
                            // Ignore watcher creation failures
                        }
                    }
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(directory)
                    {
                        Filter = fileName,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = false
                    };
                    watcher.Created += OnFileSystemChanged;
                    watcher.Deleted += OnFileSystemChanged;
                    watcher.Changed += OnFileSystemChanged;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch
                {
                    // Ignore watcher creation failures (e.g., network paths)
                }
            }
        }

        private void OnDirectoryCreated(object sender, FileSystemEventArgs e)
        {
            // A directory we were waiting for was created - schedule refresh to pick up any new configs
            DebouncedRefresh();
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            DebouncedRefresh();
        }

        private void DebouncedRefresh()
        {
            lock (_debounceLock)
            {
                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;

                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        // Wait 200ms for additional changes before refreshing
                        await System.Threading.Tasks.Task.Delay(200, token);

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);

                        if (!IsDisposed)
                        {
                            RefreshChildren();
                        }
                    }
                    catch (System.Threading.Tasks.TaskCanceledException)
                    {
                        // Debounce cancelled
                    }
                });
            }
        }

        protected override void OnDisposing()
        {
            lock (_debounceLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }

            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();

            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }
            _children.Clear();
        }
    }
}
