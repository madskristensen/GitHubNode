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
    /// The GitHub node always appears when a solution is open, even if the .github folder
    /// does not exist yet. If a .github folder exists in the solution directory or any
    /// parent directory, that folder is used; otherwise, the node represents a potential
    /// .github folder in the solution directory.
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Export(typeof(GitHubSourceProvider))]
    [Name(nameof(GitHubSourceProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    [Order(Before = "GraphSearchProvider")]
    [Order(After = "WorkspaceItemNode")] // as defined in https://github.com/madskristensen/WorkspaceFiles
    internal class GitHubSourceProvider : IAttachedCollectionSourceProvider
    {
        private GitHubRootNode _rootNode;
        private GitHubSolutionCollectionSource _solutionCollectionSource;
        private string _cachedGitHubPath;
        private readonly DTE _dte;

        /// <summary>
        /// Gets the root node for the GitHub tree.
        /// Used by the search provider to enumerate items.
        /// </summary>
        public GitHubRootNode RootNode => _rootNode;

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
            // Attach to GitHubRootNode - provides its children and supports ContainedBy for search
            else if (item is GitHubRootNode)
            {
                yield return Relationships.Contains;
                yield return Relationships.ContainedBy;
            }
            // Attach to GitHubFolderNode - provides its children and supports ContainedBy for search
            else if (item is GitHubFolderNode)
            {
                yield return Relationships.Contains;
                yield return Relationships.ContainedBy;
            }
            // Attach to GitHubFileNode - supports ContainedBy for search
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

                            // Reuse existing collection source if possible to avoid breaking tree binding
                            if (_solutionCollectionSource == null)
                            {
                                _solutionCollectionSource = new GitHubSolutionCollectionSource(hierarchyItem, _rootNode);
                            }
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
            else if (relationshipName == KnownRelationships.ContainedBy)
            {
                // ContainedBy is used during Solution Explorer search to trace items back to their parents.
                // First check if the item already has a ContainedByCollection set (from search provider).
                if (item is GitHubNodeBase node)
                {
                    // Return pre-set collection if available, otherwise create one
                    return node.ContainedByCollection ?? new ContainedByCollection(node, node.ParentItem);
                }
            }

            return null;
        }

                /// <summary>
                /// Gets the path to the .github folder. If an existing .github folder is found
                /// in the solution directory or any parent directory, that path is returned.
                /// Otherwise, returns the path where .github would be created in the solution directory.
                /// </summary>
                private static string GetGitHubFolderPath(string solutionPath)
                {
                    var solutionDirectory = Path.GetDirectoryName(solutionPath);
                    if (string.IsNullOrEmpty(solutionDirectory))
                    {
                        return null;
                    }

                    // First, try to find an existing .github folder
                    var existingPath = FindExistingGitHubFolder(solutionDirectory);
                    if (existingPath != null)
                    {
                        return existingPath;
                    }

                    // No existing .github folder found - return the path where it would be created
                    return Path.Combine(solutionDirectory, ".github");
                }

                /// <summary>
                /// Finds an existing .github folder in the given directory or any parent directory.
                /// </summary>
                private static string FindExistingGitHubFolder(string directory)
                {
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
