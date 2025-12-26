using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        private readonly string _fileName;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IInvocationPattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public GitHubFileNode(string filePath, object parent)
            : base(parent)
        {
            FilePath = filePath;
            _fileName = Path.GetFileName(filePath);
        }

        /// <summary>
        /// Gets the full path to this file.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Checks if the file exists on disk.
        /// </summary>
        public bool FileExists => File.Exists(FilePath);

        // ITreeDisplayItem
        public override string Text => _fileName;
        public override string ToolTipText => FilePath;
        public override string StateToolTipText => FileExists ? string.Empty : "File not found";
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
        public ImageMoniker StateIconMoniker => FileExists ? default : KnownMonikers.StatusWarning;

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

        private static ImageMoniker GetFileIcon(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var cacheKey = string.IsNullOrEmpty(extension) ? fileName.ToLowerInvariant() : extension;

            return _fileIconCache.GetOrAdd(cacheKey, _ =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ImageMoniker moniker = _imageService.GetImageMonikerForFile(filePath);
                return moniker.Id < 0 ? KnownMonikers.Document : moniker;
            });
        }
    }
}
