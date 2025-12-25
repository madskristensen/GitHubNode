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

        /// <summary>
        /// Well-known file names mapped to specific icons.
        /// </summary>
        private static readonly Dictionary<string, ImageMoniker> _knownFileIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            // Copilot instructions
            ["copilot-instructions.md"] = KnownMonikers.DocumentOutline,

            // GitHub Actions / Dependabot
            ["dependabot.yml"] = KnownMonikers.NuGet,
            ["dependabot.yaml"] = KnownMonikers.NuGet,

            // Code owners and maintainers
            ["CODEOWNERS"] = KnownMonikers.Team,
            ["OWNERS"] = KnownMonikers.Team,

            // Funding
            ["FUNDING.yml"] = KnownMonikers.Currency,
            ["FUNDING.yaml"] = KnownMonikers.Currency,

            // Security
            ["SECURITY.md"] = KnownMonikers.Lock,

            // Contributing guidelines
            ["CONTRIBUTING.md"] = KnownMonikers.DocumentGroup,

            // Issue and PR templates
            ["ISSUE_TEMPLATE.md"] = KnownMonikers.Bug,
            ["PULL_REQUEST_TEMPLATE.md"] = KnownMonikers.PullRequest,

            // Code of conduct
            ["CODE_OF_CONDUCT.md"] = KnownMonikers.Flag,
        };
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

            // Check for well-known file names first
            if (_knownFileIcons.TryGetValue(fileName, out ImageMoniker knownIcon))
            {
                return knownIcon;
            }

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
