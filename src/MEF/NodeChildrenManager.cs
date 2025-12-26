using System.Collections.ObjectModel;
using System.IO;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Manages file system watching and child collection refresh for GitHub nodes.
    /// Encapsulates common logic shared between GitHubRootNode and GitHubFolderNode.
    /// </summary>
    internal sealed class NodeChildrenManager : IDisposable
    {
        private readonly string _folderPath;
        private readonly object _parent;
        private readonly ObservableCollection<object> _children;
        private readonly Action _onPropertyChanged;
        private readonly bool _includeSubdirectories;
        private FileSystemWatcher _watcher;
        private bool _disposed;

        /// <summary>
        /// Creates a new NodeChildrenManager.
        /// </summary>
        /// <param name="folderPath">The folder path to watch.</param>
        /// <param name="parent">The parent node for creating child nodes.</param>
        /// <param name="children">The observable collection to populate with children.</param>
        /// <param name="onPropertyChanged">Action to invoke when properties change.</param>
        /// <param name="includeSubdirectories">Whether to watch subdirectories recursively.</param>
        public NodeChildrenManager(
            string folderPath,
            object parent,
            ObservableCollection<object> children,
            Action onPropertyChanged,
            bool includeSubdirectories = false)
        {
            _folderPath = folderPath;
            _parent = parent;
            _children = children;
            _onPropertyChanged = onPropertyChanged;
            _includeSubdirectories = includeSubdirectories;
        }

        /// <summary>
        /// Gets whether there are any children.
        /// </summary>
        public bool HasItems => _children.Count > 0;

        /// <summary>
        /// Initializes the manager by refreshing children and setting up the file watcher.
        /// Must be called on the UI thread.
        /// </summary>
        public void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RefreshChildren();
            SetupFileWatcher();
        }

        /// <summary>
        /// Refreshes the children collection from the file system.
        /// Must be called on the UI thread.
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

            if (!Directory.Exists(_folderPath))
                return;

            // Add subdirectories first
            foreach (var dir in Directory.GetDirectories(_folderPath))
            {
                _children.Add(new GitHubFolderNode(dir, _parent));
            }

            // Then add files
            foreach (var file in Directory.GetFiles(_folderPath))
            {
                _children.Add(new GitHubFileNode(file, _parent));
            }

            _onPropertyChanged?.Invoke();
        }

        private void SetupFileWatcher()
        {
            if (!Directory.Exists(_folderPath))
                return;

            _watcher = new FileSystemWatcher(_folderPath)
            {
                IncludeSubdirectories = _includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.EnableRaisingEvents = true;
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            // FireAndForget is appropriate here since this is a fire-and-forget file system event
            #pragma warning disable VSSDK007 // Use ThreadHelper.JoinableTaskFactory.RunAsync (fire and forget)
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RefreshChildren();
            });
            #pragma warning restore VSSDK007
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            // Handle renames by updating the node in-place instead of refreshing all children
            #pragma warning disable VSSDK007 // Use ThreadHelper.JoinableTaskFactory.RunAsync (fire and forget)
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Only handle renames for direct children of this folder
                var parentDir = Path.GetDirectoryName(e.OldFullPath);
                if (!string.Equals(parentDir, _folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    // This rename is in a subdirectory - let that folder's manager handle it
                    return;
                }

                // Try to find and update the renamed node
                foreach (var child in _children)
                {
                    if (child is GitHubFileNode fileNode && 
                        string.Equals(fileNode.FilePath, e.OldFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        fileNode.UpdatePath(e.FullPath);
                        return;
                    }

                    if (child is GitHubFolderNode folderNode && 
                        string.Equals(folderNode.FolderPath, e.OldFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        folderNode.UpdatePath(e.FullPath);
                        return;
                    }
                }

                // Node not found in our children - shouldn't happen for direct children
                RefreshChildren();
            });
            #pragma warning restore VSSDK007
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }
            _children.Clear();
        }
    }
}
