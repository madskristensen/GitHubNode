using System.Collections;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// A collection source that returns a single parent item for the ContainedBy relationship.
    /// This enables Solution Explorer search to trace items back to their parents,
    /// allowing the search results to display the full hierarchy path.
    /// </summary>
    /// <remarks>
    /// The ContainedBy relationship works as follows:
    /// - SourceItem is the child item (the item that is "contained by" something)
    /// - Items contains the parent item(s) that contain the child
    /// </remarks>
    internal sealed class ContainedByCollection : IAttachedCollectionSource
    {
        private readonly object[] _items;

        /// <summary>
        /// Creates a ContainedBy collection for the given child item.
        /// </summary>
        /// <param name="child">The child item that is contained by the parent.</param>
        /// <param name="parent">The parent item that contains the child.</param>
        public ContainedByCollection(object child, object parent)
        {
            SourceItem = child;
            _items = parent != null ? new[] { parent } : new object[0];
        }

        /// <summary>
        /// The child item that is contained by the parent(s) in Items.
        /// </summary>
        public object SourceItem { get; }

        public bool HasItems => _items.Length > 0;

        /// <summary>
        /// The parent item(s) that contain the SourceItem.
        /// </summary>
        public IEnumerable Items => _items;
    }
}
