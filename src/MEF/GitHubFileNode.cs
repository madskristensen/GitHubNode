using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using GitHubNode.Services;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Represents a file within the .github folder hierarchy in Solution Explorer.
    /// </summary>
    internal sealed class GitHubFileNode :
        GitHubNodeBase,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IInvocationPattern,
        IContextMenuPattern
    {
        private static readonly ConcurrentDictionary<string, ImageMoniker> _fileIconCache = new();
        private static readonly IVsImageService2 _imageService = VS.GetRequiredService<SVsImageService, IVsImageService2>();
        private string _fileName;
        private GitFileStatus _cachedGitStatus = GitFileStatus.NotInRepo;
        private bool _gitStatusLoaded;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IInvocationPattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public GitHubFileNode(string filePath, object parent)
            : this(filePath, parent, forSearchOnly: false)
        {
        }

        /// <summary>
        /// Creates a GitHubFileNode, optionally as a lightweight search-only node.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <param name="parent">The parent node.</param>
        /// <param name="forSearchOnly">If true, skips loading git status (for search-only nodes).</param>
        internal GitHubFileNode(string filePath, object parent, bool forSearchOnly)
            : base(parent)
        {
            FilePath = filePath;
            _fileName = Path.GetFileName(filePath);

            // Skip git status loading for search-only nodes to avoid unnecessary work
            if (!forSearchOnly)
            {
                LoadGitStatusAsync().FireAndForget();
            }
        }

        /// <summary>
        /// Gets the full path to this file.
        /// </summary>
        public string FilePath { get; private set; }

        /// <summary>
        /// Updates the file path after a rename operation.
        /// </summary>
        public void UpdatePath(string newPath)
        {
            FilePath = newPath;
            _fileName = Path.GetFileName(newPath);
            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(ToolTipText));
            RaisePropertyChanged(nameof(IconMoniker));

            // Reload Git status for new path
            LoadGitStatusAsync().FireAndForget();
        }

        /// <summary>
        /// Refreshes the Git status icon asynchronously.
        /// </summary>
        public void RefreshGitStatus()
        {
            LoadGitStatusAsync().FireAndForget();
        }

        /// <summary>
        /// Checks if the file exists on disk.
        /// </summary>
        public bool FileExists => File.Exists(FilePath);

        // ITreeDisplayItem
        public override string Text => _fileName;
        public override string ToolTipText => FilePath;
        public override string StateToolTipText => FileExists ? GetGitStatusTooltip() : "File not found";
        public override bool IsCut => !FileExists;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                return !FileExists ? KnownMonikers.DocumentWarning : GetFileIcon(FilePath);
            }
        }

        public ImageMoniker ExpandedIconMoniker
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return IconMoniker;
            }
        }

        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker
        {
            get
            {
                if (!FileExists)
                {
                    return KnownMonikers.StatusWarning;
                }

                // Return cached status - it will be updated asynchronously
                return GitStatusService.GetStatusIcon(_cachedGitStatus);
            }
        }

        // IPrioritizedComparable - Files appear after folders
        public int Priority => 1;

        public int CompareTo(object obj)
        {
            return obj is ITreeDisplayItem other ? StringComparer.OrdinalIgnoreCase.Compare(Text, other.Text) : 0;
        }

        // IInvocationPattern
        public IInvocationController InvocationController => GitHubInvocationController.Instance;
        public bool CanPreview => FileExists;

        // IContextMenuPattern
        public IContextMenuController ContextMenuController => GitHubContextMenuController.Instance;

        private async Task LoadGitStatusAsync()
        {
            if (IsDisposed)
            {
                return;
            }

            GitFileStatus status = await GitStatusService.GetFileStatusAsync(FilePath);

            if (IsDisposed)
            {
                return;
            }

            var statusChanged = _cachedGitStatus != status || !_gitStatusLoaded;
            _cachedGitStatus = status;
            _gitStatusLoaded = true;

            if (statusChanged)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (!IsDisposed)
                {
                    RaisePropertyChanged(nameof(StateIconMoniker));
                    RaisePropertyChanged(nameof(StateToolTipText));
                }
            }
        }

        private string GetGitStatusTooltip()
        {
            switch (_cachedGitStatus)
            {
                case GitFileStatus.Unmodified:
                    return "Unchanged";
                case GitFileStatus.Modified:
                    return "Modified";
                case GitFileStatus.Staged:
                    return "Staged";
                case GitFileStatus.Added:
                    return "Added";
                case GitFileStatus.Untracked:
                    return "Untracked";
                case GitFileStatus.Deleted:
                    return "Deleted";
                case GitFileStatus.Conflict:
                    return "Conflict";
                case GitFileStatus.Ignored:
                    return "Ignored";
                case GitFileStatus.Renamed:
                    return "Renamed";
                default:
                    return string.Empty;
            }
        }

        private static ImageMoniker GetFileIcon(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var cacheKey = string.IsNullOrEmpty(extension) ? fileName.ToLowerInvariant() : extension;

            return _fileIconCache.GetOrAdd(cacheKey, key =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                // Use a fake path with just the extension to avoid locking the actual file
                // The image service determines the icon based on extension, not file contents
                var fakePath = string.IsNullOrEmpty(extension)
                    ? fileName  // For extensionless files like CODEOWNERS, use the filename
                    : "file" + extension;

                ImageMoniker moniker = _imageService.GetImageMonikerForFile(fakePath);
                return moniker.Id < 0 ? KnownMonikers.Document : moniker;
            });
        }
    }
}
