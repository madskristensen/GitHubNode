using System.Collections.Generic;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Base class for all MCP nodes in Solution Explorer.
    /// </summary>
    internal abstract class McpNodeBase : SolutionExplorerNodeBase
    {
        protected McpNodeBase(object sourceItem, object parentItem)
            : base(sourceItem, parentItem)
        {
        }
    }
}
