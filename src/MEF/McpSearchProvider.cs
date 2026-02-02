using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using GitHubNode.Services;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Utilities;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Provides search results for MCP nodes in Solution Explorer.
    /// </summary>
    /// <remarks>
    /// This search provider enables Solution Explorer's search box to find MCP server configurations
    /// in the MCP Servers tree. When a match is found, it uses the ContainedBy relationship
    /// to trace back to parent nodes so the full path can be displayed.
    ///
    /// Performance optimizations:
    /// - Breadth-first search yields results at each level before going deeper
    /// - Parallel processing at each level
    /// - Respects cancellation token for user cancellation
    /// - Early termination when result limit is reached
    /// </remarks>
    [Export(typeof(ISearchProvider))]
    [Name(nameof(McpSearchProvider))]
    [Order(Before = "GraphSearchProvider")]
    [method: ImportingConstructor]
    internal sealed class McpSearchProvider([Import] McpSourceProvider sourceProvider) : ISearchProvider
    {

        /// <summary>
        /// Maximum number of search results to return. Prevents performance issues in very large trees.
        /// </summary>
        private const int _maxSearchResults = 200;

        public void Search(IRelationshipSearchParameters parameters, Action<ISearchResult> resultAccumulator)
        {
            if (parameters == null || resultAccumulator == null)
            {
                return;
            }

            var searchPattern = parameters.SearchQuery?.SearchString;
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                return;
            }

            if (!McpSettingsService.IsMcpServersEnabled())
            {
                return;
            }

            // Get the cancellation token from parameters - VS will cancel when user clears search
            CancellationToken cancellationToken = parameters.CancellationToken;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Ensure the root node exists - this is needed for ContainedBy relationship to work
            McpRootNode rootNode = sourceProvider.RootNode;
            if (rootNode == null)
            {
                // Root node doesn't exist yet (tree not expanded)
                return;
            }

            if (!rootNode.HasItems)
            {
                return;
            }

            // Use GetChildrenForSearch to get items
            IEnumerable<McpNodeBase> items = rootNode.GetChildrenForSearch();
            if (items == null)
            {
                return;
            }

            SearchBreadthFirstParallel(rootNode, items, searchPattern, resultAccumulator, cancellationToken);
        }

        /// <summary>
        /// Performs breadth-first search with parallel processing at each level.
        /// </summary>
        private static void SearchBreadthFirstParallel(
            McpRootNode rootNode,
            IEnumerable<McpNodeBase> rootItems,
            string searchPattern,
            Action<ISearchResult> resultAccumulator,
            CancellationToken cancellationToken)
        {
            var resultCount = 0;

            // Check if the root node itself matches
            if (MatchesSearch(rootNode.Text, searchPattern))
            {
                SetupContainedByChain(rootNode);
                resultAccumulator(new McpSearchResult(rootNode));
                resultCount++;
            }

            // Queue of nodes to process at the current level
            var currentLevel = new List<McpNodeBase>(rootItems);

            while (currentLevel.Count > 0 && resultCount < _maxSearchResults && !cancellationToken.IsCancellationRequested)
            {
                // Collect results and children from the current level in parallel
                var results = new ConcurrentBag<McpNodeBase>();
                var nextLevel = new ConcurrentBag<McpNodeBase>();

                // Process all nodes at this level in parallel
                try
                {
                    Parallel.ForEach(
                        currentLevel,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Environment.ProcessorCount,
                            CancellationToken = cancellationToken
                        },
                        (node, loopState) =>
                        {
                            // Check early termination
                            if (Volatile.Read(ref resultCount) >= _maxSearchResults || cancellationToken.IsCancellationRequested)
                            {
                                loopState.Stop();
                                return;
                            }

                            // Skip hint nodes in search results
                            if (node is McpHintNode)
                            {
                                return;
                            }

                            // Check if this node matches
                            if (MatchesSearch(node.Text, searchPattern))
                            {
                                results.Add(node);
                            }

                            // If this is a config node, get its children for the next level
                            if (node is McpConfigNode configNode)
                            {
                                foreach (McpNodeBase child in configNode.GetChildrenForSearch())
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        break;
                                    }
                                    nextLevel.Add(child);
                                }
                            }
                        });
                }
                catch (OperationCanceledException)
                {
                    // Search was cancelled by user - exit gracefully
                    return;
                }

                // Check cancellation before emitting results
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Emit results from this level (on the calling thread for thread safety)
                foreach (McpNodeBase match in results)
                {
                    if (resultCount >= _maxSearchResults || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    SetupContainedByChain(match);
                    resultAccumulator(new McpSearchResult(match));
                    resultCount++;
                }

                // Move to the next level
                currentLevel = [.. nextLevel];
            }
        }

        /// <summary>
        /// Sets up the ContainedBy collection chain from the given node back to the solution.
        /// This pre-populates the parent relationship so VS doesn't need to call the source provider.
        /// </summary>
        private static void SetupContainedByChain(McpNodeBase node)
        {
            // Walk up the parent chain, setting ContainedByCollection on each item
            object current = node;

            while (current != null)
            {
                if (current is McpNodeBase nodeBase)
                {
                    // Set the ContainedBy collection if not already set
                    nodeBase.ContainedByCollection ??= new ContainedByCollection(nodeBase, nodeBase.ParentItem);
                    current = nodeBase.ParentItem;
                }
                else
                {
                    // Unknown type (likely IVsHierarchyItem for solution) - stop
                    break;
                }
            }
        }

        private static bool MatchesSearch(string text, string searchPattern)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            // Case-insensitive substring match (consistent with Solution Explorer behavior)
            return text.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Represents a search result for an MCP node.
    /// </summary>
    internal sealed class McpSearchResult(McpNodeBase node) : ISearchResult
    {
        public object GetDisplayItem()
        {
            // Return the node itself as the display item - it implements ITreeDisplayItem
            return node;
        }
    }
}
