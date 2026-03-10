using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Represents the user profile .github folder (%USERPROFILE%\.github) in Solution Explorer.
    /// This is a virtual node that appears as a child of the GitHub root node, providing
    /// access to global Copilot settings that apply across all solutions.
    /// </summary>
    internal sealed class GitHubUserProfileNode :
        GitHubNodeBase,
        IAttachedCollectionSource,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IContextMenuPattern
    {
        private readonly ObservableCollection<object> _children;
        private readonly NodeChildrenManager _childrenManager;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public GitHubUserProfileNode(object parent)
            : base(parent)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            FolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".copilot");

            _children = [];
            _childrenManager = new NodeChildrenManager(
                FolderPath,
                this,
                _children,
                () =>
                {
                    RaisePropertyChanged(nameof(HasItems));
                    RaisePropertyChanged(nameof(Items));
                },
                includeSubdirectories: false);

            _childrenManager.Initialize();
        }

        /// <summary>
        /// Gets the full path to the user profile .github folder.
        /// </summary>
        public string FolderPath { get; }

        // IAttachedCollectionSource
        public bool HasItems => _childrenManager.HasItems;
        public IEnumerable Items => _children;

        /// <summary>
        /// Gets the children for search enumeration without modifying the tree.
        /// </summary>
        public IEnumerable<GitHubNodeBase> GetChildrenForSearch()
        {
            // If children have already been loaded, return them
            if (_children.Count > 0)
            {
                return _children.OfType<GitHubNodeBase>().ToList();
            }

            // For unexpanded node, enumerate file system directly
            if (!Directory.Exists(FolderPath))
            {
                return Enumerable.Empty<GitHubNodeBase>();
            }

            return EnumerateChildrenForSearch();
        }

        /// <summary>
        /// Lazily enumerates children for search.
        /// </summary>
        private IEnumerable<GitHubNodeBase> EnumerateChildrenForSearch()
        {
            string[] directories;
            string[] files;

            try
            {
                directories = Directory.GetDirectories(FolderPath);
                files = Directory.GetFiles(FolderPath);
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }
            catch (DirectoryNotFoundException)
            {
                yield break;
            }
            catch (IOException)
            {
                yield break;
            }

            // Return folders first
            foreach (var dir in directories)
            {
                GitHubFolderNode node;
                try
                {
                    node = new GitHubFolderNode(dir, this, forSearchOnly: true);
                }
                catch
                {
                    continue;
                }
                yield return node;
            }

            // Then files
            foreach (var file in files)
            {
                GitHubFileNode node;
                try
                {
                    node = new GitHubFileNode(file, this, forSearchOnly: true);
                }
                catch
                {
                    continue;
                }
                yield return node;
            }
        }

        // ITreeDisplayItem
        public override string Text => "User Profile";
        public override string ToolTipText => $"Global Copilot settings: {FolderPath}\n\nSettings here apply to all solutions for this user.";
        public override System.Windows.FontWeight FontWeight => System.Windows.FontWeights.SemiBold;

        // ITreeDisplayItemWithImages - Use a user/home icon to distinguish from regular folders
        public ImageMoniker IconMoniker => KnownMonikers.User;
        public ImageMoniker ExpandedIconMoniker => KnownMonikers.User;
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        // IPrioritizedComparable - Priority -1 to appear before folders (which have priority 0)
        public int Priority => -1;

        public int CompareTo(object obj)
        {
            if (obj is IPrioritizedComparable other)
            {
                return Priority.CompareTo(other.Priority);
            }

            return -1; // Always appear first
        }

        // IContextMenuPattern
        public IContextMenuController ContextMenuController => GitHubContextMenuController.Instance;

        protected override void OnDisposing()
        {
            _childrenManager?.Dispose();
        }
    }
}
