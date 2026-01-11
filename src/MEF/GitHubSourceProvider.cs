using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using EnvDTE;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Utilities;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Provides the GitHub node as a child of the solution node in Solution Explorer.
    /// The GitHub node appears when the solution is in a Git repository, even if
    /// the .github folder doesn't exist yet.
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Name(nameof(GitHubSourceProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    internal class GitHubSourceProvider : IAttachedCollectionSourceProvider
    {
        private GitHubRootNode _rootNode;
        private GitHubSolutionCollectionSource _solutionCollectionSource;
        private string _cachedGitHubPath;
        private readonly DTE _dte;

        public GitHubSourceProvider()
        {
            _dte = VS.GetRequiredService<DTE, DTE>();
            VS.Events.SolutionEvents.OnBeforeCloseSolution += OnBeforeCloseSolution;
        }

        private void OnBeforeCloseSolution()
        {
            _solutionCollectionSource?.Dispose();
            _solutionCollectionSource = null;
            _rootNode?.Dispose();
            _rootNode = null;
            _cachedGitHubPath = null;
        }

        public IEnumerable<IAttachedRelationship> GetRelationships(object item)
        {
            // Attach to the solution node - provides the GitHub root node
            if (item is IVsHierarchyItem hierarchyItem &&
                HierarchyUtilities.IsSolutionNode(hierarchyItem.HierarchyIdentity))
            {
                yield return Relationships.Contains;
            }
            // Attach to GitHubRootNode - provides its children (files and folders)
            else if (item is GitHubRootNode)
            {
                yield return Relationships.Contains;
            }
            // Attach to GitHubFolderNode - provides its children (files and subfolders)
            else if (item is GitHubFolderNode)
            {
                yield return Relationships.Contains;
            }
        }

        public IAttachedCollectionSource CreateCollectionSource(object item, string relationshipName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (relationshipName == KnownRelationships.Contains)
            {
                // For the solution node, return a wrapper that contains the GitHub root node
                if (item is IVsHierarchyItem hierarchyItem &&
                    HierarchyUtilities.IsSolutionNode(hierarchyItem.HierarchyIdentity))
                {
                    var solutionPath = _dte?.Solution?.FullName;
                    if (!string.IsNullOrEmpty(solutionPath))
                    {
                        var gitHubPath = GetGitHubFolderPath(solutionPath);
                        if (gitHubPath != null)
                        {
                            if (_rootNode == null || _cachedGitHubPath != gitHubPath)
                            {
                                _rootNode?.Dispose();
                                _cachedGitHubPath = gitHubPath;
                                _rootNode = new GitHubRootNode(hierarchyItem, gitHubPath);
                            }

                            _solutionCollectionSource?.Dispose();
                            _solutionCollectionSource = new GitHubSolutionCollectionSource(hierarchyItem, _rootNode);
                            return _solutionCollectionSource;
                        }
                    }
                }
                // For the root node, return itself (it contains the items)
                else if (item is GitHubRootNode rootNode)
                {
                    return rootNode;
                }
                // For folder nodes, return itself (it contains child items)
                else if (item is GitHubFolderNode folderNode)
                {
                    return folderNode;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the .github folder path. If the folder doesn't exist but the solution
        /// is in a Git repository, returns the expected path where .github should be created.
        /// </summary>
        private static string GetGitHubFolderPath(string solutionPath)
        {
            var directory = Path.GetDirectoryName(solutionPath);

            // First, check if .github already exists anywhere up the tree
            var current = directory;
            while (!string.IsNullOrEmpty(current))
            {
                var gitHubPath = Path.Combine(current, ".github");
                if (Directory.Exists(gitHubPath))
                {
                    return gitHubPath;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            // .github doesn't exist - find the Git root and return the expected path
            var gitRoot = FindGitRoot(directory);
            if (gitRoot != null)
            {
                return Path.Combine(gitRoot, ".github");
            }

            return null;
        }

        /// <summary>
        /// Finds the Git repository root by looking for a .git folder.
        /// </summary>
        private static string FindGitRoot(string path)
        {
            var current = path;

            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, ".git")))
                {
                    return current;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            return null;
        }
    }
}
