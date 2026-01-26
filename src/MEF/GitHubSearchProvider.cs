using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Utilities;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Provides search results for GitHub nodes in Solution Explorer.
    /// </summary>
    /// <remarks>
    /// This search provider enables Solution Explorer's search box to find files and folders
    /// in the GitHub tree. When a match is found, it uses the ContainedBy relationship
    /// to trace back to parent nodes so the full path can be displayed.
    ///
    /// Performance optimizations:
    /// - Breadth-first search yields results at each level before going deeper
    /// - Parallel processing of subfolders at each level
    /// - Respects cancellation token for user cancellation
    /// - Early termination when result limit is reached
    /// </remarks>
    [Export(typeof(ISearchProvider))]
    [Name(nameof(GitHubSearchProvider))]
    [Order(Before = "GraphSearchProvider")]
    internal sealed class GitHubSearchProvider : ISearchProvider
    {
        private readonly GitHubSourceProvider _sourceProvider;

        /// <summary>
        /// Maximum number of search results to return. Prevents performance issues in very large trees.
        /// </summary>
        private const int _maxSearchResults = 200;

        [ImportingConstructor]
        public GitHubSearchProvider([Import] GitHubSourceProvider sourceProvider)
        {
            _sourceProvider = sourceProvider;
        }

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

            // Get the cancellation token from parameters - VS will cancel when user clears search
            CancellationToken cancellationToken = parameters.CancellationToken;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Ensure the root node exists - this is needed for ContainedBy relationship to work
            GitHubRootNode rootNode = _sourceProvider.RootNode;
            if (rootNode == null)
            {
                // Root node doesn't exist yet (tree not expanded)
                return;
            }

            if (!rootNode.HasItems)
            {
                return;
            }

            // Get items from the root node's Items collection
            // Cast each item since we know they're all GitHubNodeBase derived types
            var items = new List<GitHubNodeBase>();
            foreach (object item in rootNode.Items)
            {
                if (item is GitHubNodeBase node)
                {
                    items.Add(node);
                }
            }

            if (items.Count == 0)
            {
                return;
            }

            SearchBreadthFirstParallel(rootNode, items, searchPattern, resultAccumulator, cancellationToken);
        }

        /// <summary>
        /// Performs breadth-first search with parallel processing of folders at each level.
        /// This yields results faster by searching shallower levels first and processing
        /// multiple folders in parallel.
        /// </summary>
        private static void SearchBreadthFirstParallel(
            GitHubRootNode rootNode,
            IEnumerable<GitHubNodeBase> rootItems,
            string searchPattern,
            Action<ISearchResult> resultAccumulator,
            CancellationToken cancellationToken)
        {
            var resultCount = 0;

            // Check if the root node itself matches
            if (MatchesSearch(rootNode.Text, searchPattern))
            {
                SetupContainedByChain(rootNode, rootNode);
                resultAccumulator(new GitHubSearchResult(rootNode));
                resultCount++;
            }

            // Queue of nodes to process at the current level
            var currentLevel = new List<GitHubNodeBase>(rootItems);

            while (currentLevel.Count > 0 && resultCount < _maxSearchResults && !cancellationToken.IsCancellationRequested)
            {
                // Collect results and children from the current level in parallel
                var results = new ConcurrentBag<GitHubNodeBase>();
                var nextLevel = new ConcurrentBag<GitHubNodeBase>();

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

                            // Check if this node matches
                            if (MatchesSearch(node.Text, searchPattern))
                            {
                                results.Add(node);
                            }

                            // If this is a folder, get its children for the next level
                            if (node is GitHubFolderNode folderNode)
                            {
                                foreach (GitHubNodeBase child in folderNode.GetChildrenForSearch())
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
                foreach (GitHubNodeBase match in results)
                {
                    if (resultCount >= _maxSearchResults || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    SetupContainedByChain(match, rootNode);
                    resultAccumulator(new GitHubSearchResult(match));
                    resultCount++;
                }

                // Move to the next level
                currentLevel = nextLevel.ToList();
            }
        }

        /// <summary>
        /// Sets up the ContainedBy collection chain from the given node back to the solution.
        /// This pre-populates the parent relationship so VS doesn't need to call the source provider.
        /// </summary>
        private static void SetupContainedByChain(GitHubNodeBase node, GitHubRootNode rootNode)
        {
            // Walk up the parent chain, setting ContainedByCollection on each item
            object current = node;

            while (current != null)
            {
                if (current is GitHubFileNode fileNode)
                {
                    // Set the ContainedBy collection if not already set
                    fileNode.ContainedByCollection ??= new ContainedByCollection(fileNode, fileNode.ParentItem);
                    current = fileNode.ParentItem;
                }
                else if (current is GitHubFolderNode folderNode)
                {
                    // Set the ContainedBy collection if not already set
                    folderNode.ContainedByCollection ??= new ContainedByCollection(folderNode, folderNode.ParentItem);
                    current = folderNode.ParentItem;
                }
                else if (current is GitHubRootNode rn)
                {
                    // Set the ContainedBy collection for the root node to point to solution
                    rn.ContainedByCollection ??= new ContainedByCollection(rn, rn.ParentItem);
                    // Stop - we've reached the root
                    break;
                }
                else
                {
                    // Unknown type, stop
                    break;
                }
            }

            // Always ensure the root node's ContainedBy is set up
            rootNode.ContainedByCollection ??= new ContainedByCollection(rootNode, rootNode.ParentItem);
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
    /// Represents a search result for a GitHub node.
    /// </summary>
    internal sealed class GitHubSearchResult : ISearchResult
    {
        private readonly GitHubNodeBase _node;

        public GitHubSearchResult(GitHubNodeBase node)
        {
            _node = node;
        }

        public object GetDisplayItem()
        {
            // Return the node itself as the display item - it implements ITreeDisplayItem
            return _node;
        }
    }
}
