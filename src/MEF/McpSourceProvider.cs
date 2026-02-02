using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using EnvDTE;
using GitHubNode.Services;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Utilities;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Provides the MCP Servers node as a child of the solution node in Solution Explorer.
    /// The MCP Servers node appears as a sibling to the GitHub node.
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Export(typeof(McpSourceProvider))]
    [Name(nameof(McpSourceProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    [Order(Before = "GraphSearchProvider")]
    [Order(After = nameof(GitHubSourceProvider))]
    internal class McpSourceProvider : IAttachedCollectionSourceProvider
    {
        public static McpSourceProvider Instance { get; private set; }
        private McpRootNode _rootNode;
        private McpSolutionCollectionSource _solutionCollectionSource;
        private string _cachedSolutionDirectory;
        private readonly DTE _dte;

        /// <summary>
        /// Gets the root node for the MCP Servers tree.
        /// Used by the search provider to enumerate items.
        /// </summary>
        public McpRootNode RootNode => _rootNode;

        public McpSourceProvider()
        {
            Instance = this;
            _dte = VS.GetRequiredService<DTE, DTE>();
            VS.Events.SolutionEvents.OnBeforeCloseSolution += OnBeforeCloseSolution;
        }

        private void OnBeforeCloseSolution()
        {
            _solutionCollectionSource?.Dispose();
            _solutionCollectionSource = null;
            _rootNode?.Dispose();
            _rootNode = null;
            _cachedSolutionDirectory = null;
        }

        public void UpdateVisibility()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_solutionCollectionSource == null)
            {
                return;
            }

            if (_solutionCollectionSource.SourceItem is not IVsHierarchyItem hierarchyItem)
            {
                return;
            }

            ApplyVisibility(hierarchyItem);
        }

        public IEnumerable<IAttachedRelationship> GetRelationships(object item)
        {
            // Attach to the solution node - provides the MCP root node
            if (item is IVsHierarchyItem hierarchyItem &&
                HierarchyUtilities.IsSolutionNode(hierarchyItem.HierarchyIdentity))
            {
                yield return Relationships.Contains;
            }
            // Attach to McpRootNode - provides its children and supports ContainedBy for search
            else if (item is McpRootNode)
            {
                yield return Relationships.Contains;
                yield return Relationships.ContainedBy;
            }
            // Attach to McpConfigNode - provides its children and supports ContainedBy for search
            else if (item is McpConfigNode)
            {
                yield return Relationships.Contains;
                yield return Relationships.ContainedBy;
            }
            // Attach to McpServerNode - supports ContainedBy for search
            else if (item is McpServerNode)
            {
                yield return Relationships.ContainedBy;
            }
            // Attach to McpHintNode - supports ContainedBy for search
            else if (item is McpHintNode)
            {
                yield return Relationships.ContainedBy;
            }
        }

        public IAttachedCollectionSource CreateCollectionSource(object item, string relationshipName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (relationshipName == KnownRelationships.Contains)
            {
                // For the solution node, return a wrapper that contains the MCP root node
                if (item is IVsHierarchyItem hierarchyItem &&
                    HierarchyUtilities.IsSolutionNode(hierarchyItem.HierarchyIdentity))
                {
                    var solutionPath = _dte?.Solution?.FullName;
                    if (!string.IsNullOrEmpty(solutionPath))
                    {
                        var solutionDirectory = Path.GetDirectoryName(solutionPath);
                        if (!string.IsNullOrEmpty(solutionDirectory))
                        {
                            _cachedSolutionDirectory = solutionDirectory;

                            ApplyVisibility(hierarchyItem);

                            // Reuse existing collection source if possible to avoid breaking tree binding
                            _solutionCollectionSource ??= new McpSolutionCollectionSource(hierarchyItem, _rootNode);
                            return _solutionCollectionSource;
                        }
                    }
                }
                // For the root node, return itself (it contains the items)
                else if (item is McpRootNode rootNode)
                {
                    return rootNode;
                }
                // For config nodes, return itself (it contains server entries)
                else if (item is McpConfigNode configNode)
                {
                    return configNode;
                }
            }
            else if (relationshipName == KnownRelationships.ContainedBy)
            {
                // ContainedBy is used during Solution Explorer search to trace items back to their parents.
                // First check if the item already has a ContainedByCollection set (from search provider).
                if (item is McpNodeBase node)
                {
                    // Return pre-set collection if available, otherwise create one
                    return node.ContainedByCollection ?? new ContainedByCollection(node, node.ParentItem);
                }
            }

            return null;
        }

        private void ApplyVisibility(IVsHierarchyItem hierarchyItem)
        {
            if (!McpSettingsService.IsMcpServersEnabled())
            {
                _solutionCollectionSource?.SetRootNode(null);
                _rootNode?.Dispose();
                _rootNode = null;
                return;
            }

            if (string.IsNullOrEmpty(_cachedSolutionDirectory))
            {
                return;
            }

            if (_rootNode == null || _cachedSolutionDirectory != _rootNode.SolutionDirectory)
            {
                _rootNode?.Dispose();
                _rootNode = new McpRootNode(hierarchyItem, _cachedSolutionDirectory);
            }

            _solutionCollectionSource?.SetRootNode(_rootNode);
        }
    }
}
