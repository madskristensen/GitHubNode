using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using EnvDTE;
using GitHubNode.Services;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Utilities;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Provides AI-related nodes as children of the solution node in Solution Explorer.
    /// The GitHub node is always shown. Additional nodes (for example .claude and .agents)
    /// are shown only when those folders exist on disk.
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Export(typeof(GitHubSourceProvider))]
    [Name(nameof(GitHubSourceProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    [Order(Before = "GraphSearchProvider")]
    [Order(After = "WorkspaceItemNode")]
    internal class GitHubSourceProvider : IAttachedCollectionSourceProvider
    {
        public static GitHubSourceProvider Instance { get; private set; }
        private readonly Dictionary<string, GitHubRootNode> _rootNodesByPath = new(System.StringComparer.OrdinalIgnoreCase);
        private GitHubSolutionCollectionSource _solutionCollectionSource;
        private IVsHierarchyItem _cachedHierarchyItem;
        private readonly DTE _dte;

        /// <summary>
        /// Gets all root nodes currently shown in the AI tree.
        /// Used by the search provider to enumerate items.
        /// </summary>
        public IReadOnlyList<GitHubRootNode> RootNodes => _rootNodesByPath.Values.ToList();

        public GitHubSourceProvider()
        {
            Instance = this;
            _dte = VS.GetRequiredService<DTE, DTE>();
            VS.Events.SolutionEvents.OnBeforeCloseSolution += OnBeforeCloseSolution;
        }

        private void OnBeforeCloseSolution()
        {
            _solutionCollectionSource?.Dispose();
            _solutionCollectionSource = null;
            _cachedHierarchyItem = null;

            foreach (GitHubRootNode rootNode in _rootNodesByPath.Values)
            {
                rootNode.Dispose();
            }

            _rootNodesByPath.Clear();
        }

        public void UpdateVisibility()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_solutionCollectionSource == null || _cachedHierarchyItem == null)
            {
                return;
            }

            if (!McpSettingsService.IsGitHubNodeEnabled())
            {
                _solutionCollectionSource.UpdateRootNodes(new List<GitHubRootNode>());

                foreach (GitHubRootNode rootNode in _rootNodesByPath.Values)
                {
                    rootNode.Dispose();
                }

                _rootNodesByPath.Clear();
                return;
            }

            string solutionPath = _dte?.Solution?.FullName;
            if (!string.IsNullOrEmpty(solutionPath))
            {
                var rootNodes = GetOrCreateRootNodes(_cachedHierarchyItem, solutionPath);
                _solutionCollectionSource.UpdateRootNodes(rootNodes);
            }
        }

        public IEnumerable<IAttachedRelationship> GetRelationships(object item)
        {
            if (item is IVsHierarchyItem hierarchyItem &&
                HierarchyUtilities.IsSolutionNode(hierarchyItem.HierarchyIdentity))
            {
                yield return Relationships.Contains;
            }
            else if (item is GitHubRootNode)
            {
                yield return Relationships.Contains;
                yield return Relationships.ContainedBy;
            }
            else if (item is GitHubUserProfileNode)
            {
                yield return Relationships.Contains;
                yield return Relationships.ContainedBy;
            }
            else if (item is GitHubFolderNode)
            {
                yield return Relationships.Contains;
                yield return Relationships.ContainedBy;
            }
            else if (item is GitHubFileNode)
            {
                yield return Relationships.ContainedBy;
            }
        }

        public IAttachedCollectionSource CreateCollectionSource(object item, string relationshipName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (relationshipName == KnownRelationships.Contains)
            {
                if (item is IVsHierarchyItem hierarchyItem &&
                    HierarchyUtilities.IsSolutionNode(hierarchyItem.HierarchyIdentity))
                {
                    _cachedHierarchyItem = hierarchyItem;

                    // Always create the collection source (possibly empty) so that later
                    // visibility toggles via UpdateVisibility() can push nodes into it.
                    // Returning null here would cause Solution Explorer to cache "no children"
                    // and never re-query us when the user enables the GitHub node.
                    IReadOnlyList<GitHubRootNode> rootNodes = [];

                    if (McpSettingsService.IsGitHubNodeEnabled())
                    {
                        string solutionPath = _dte?.Solution?.FullName;
                        if (!string.IsNullOrEmpty(solutionPath))
                        {
                            rootNodes = GetOrCreateRootNodes(hierarchyItem, solutionPath);
                        }
                    }

                    if (_solutionCollectionSource == null)
                    {
                        _solutionCollectionSource = new GitHubSolutionCollectionSource(hierarchyItem, rootNodes);
                    }
                    else
                    {
                        _solutionCollectionSource.UpdateRootNodes(rootNodes);
                    }

                    return _solutionCollectionSource;
                }
                else if (item is GitHubRootNode rootNode)
                {
                    return rootNode;
                }
                else if (item is GitHubUserProfileNode userProfileNode)
                {
                    return userProfileNode;
                }
                else if (item is GitHubFolderNode folderNode)
                {
                    return folderNode;
                }
            }
            else if (relationshipName == KnownRelationships.ContainedBy)
            {
                if (item is GitHubNodeBase node)
                {
                    return node.ContainedByCollection ?? new ContainedByCollection(node, node.ParentItem);
                }
            }

            return null;
        }

        private List<GitHubRootNode> GetOrCreateRootNodes(IVsHierarchyItem hierarchyItem, string solutionPath)
        {
            string solutionDirectory = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(solutionDirectory))
            {
                return [];
            }

            Dictionary<string, GitHubRootNode> desiredNodesByPath = new(System.StringComparer.OrdinalIgnoreCase);

            foreach (AiRootFolderDefinition definition in AiRootFolders.All)
            {
                string folderPath = ResolveFolderPath(solutionDirectory, definition);
                if (string.IsNullOrEmpty(folderPath))
                {
                    continue;
                }

                if (!_rootNodesByPath.TryGetValue(folderPath, out GitHubRootNode rootNode))
                {
                    rootNode = new GitHubRootNode(hierarchyItem, folderPath, definition);
                    _rootNodesByPath[folderPath] = rootNode;
                }

                desiredNodesByPath[folderPath] = rootNode;
            }

            foreach (string existingPath in _rootNodesByPath.Keys.ToList())
            {
                if (desiredNodesByPath.ContainsKey(existingPath))
                {
                    continue;
                }

                _rootNodesByPath[existingPath].Dispose();
                _rootNodesByPath.Remove(existingPath);
            }

            return AiRootFolders.All
                .Select(definition =>
                {
                    string path = ResolveFolderPath(solutionDirectory, definition);
                    if (string.IsNullOrEmpty(path))
                    {
                        return null;
                    }

                    desiredNodesByPath.TryGetValue(path, out GitHubRootNode rootNode);
                    return rootNode;
                })
                .Where(node => node != null)
                .ToList();
        }

        private static string ResolveFolderPath(string solutionDirectory, AiRootFolderDefinition definition)
        {
            string existingPath = FindExistingFolder(solutionDirectory, definition.FolderName);
            if (!string.IsNullOrEmpty(existingPath))
            {
                return existingPath;
            }

            return definition.AlwaysVisible
                ? Path.Combine(solutionDirectory, definition.FolderName)
                : null;
        }

        private static string FindExistingFolder(string directory, string folderName)
        {
            // Walk up at most a few levels from the solution directory. Walking all the way
            // to the drive root made the UI thread call to Directory.Exists very slow on
            // OneDrive / network locations.
            const int maxLevels = 4;
            int level = 0;

            while (!string.IsNullOrEmpty(directory) && level < maxLevels)
            {
                string folderPath = Path.Combine(directory, folderName);
                if (Directory.Exists(folderPath))
                {
                    return folderPath;
                }

                DirectoryInfo parent = Directory.GetParent(directory);
                directory = parent?.FullName;
                level++;
            }

            return null;
        }
    }
}
