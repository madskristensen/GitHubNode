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
    /// The GitHub node appears when a .github folder exists in the solution directory
    /// or any parent directory of the solution.
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Name(nameof(GitHubSourceProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    [Order(After = "WorkspaceItemNode")] // as defined in https://github.com/madskristensen/WorkspaceFiles
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
                        var gitHubPath = FindGitHubFolder(solutionPath);
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
        /// Finds the .github folder in the solution directory or any parent directory.
        /// </summary>
        private static string FindGitHubFolder(string solutionPath)
        {
            var directory = Path.GetDirectoryName(solutionPath);

            while (!string.IsNullOrEmpty(directory))
            {
                var gitHubPath = Path.Combine(directory, ".github");
                if (Directory.Exists(gitHubPath))
                {
                    return gitHubPath;
                }

                DirectoryInfo parent = Directory.GetParent(directory);
                directory = parent?.FullName;
            }

            return null;
        }
    }
}
