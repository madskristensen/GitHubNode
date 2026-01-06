using System.Collections.Generic;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Handles double-click and Enter key invocations on MCP nodes.
    /// Opens the configuration file for editing.
    /// </summary>
    internal sealed class McpInvocationController : IInvocationController
    {
        private static McpInvocationController _instance;

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static McpInvocationController Instance => _instance ??= new McpInvocationController();

        private McpInvocationController() { }

        public bool Invoke(IEnumerable<object> items, InputSource inputSource, bool preview)
        {
            foreach (var item in items)
            {
                string filePath = null;

                if (item is McpConfigNode configNode)
                {
                    filePath = configNode.FilePath;
                }
                else if (item is McpServerNode serverNode)
                {
                    filePath = serverNode.ConfigFilePath;
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    OpenFile(filePath, preview);
                }
            }

            return true;
        }

        private static void OpenFile(string filePath, bool preview)
        {
            if (!System.IO.File.Exists(filePath))
            {
                VS.MessageBox.ShowWarning(
                    "File Not Found",
                    $"The file '{filePath}' no longer exists.");
                return;
            }

            if (preview)
            {
                VS.Documents.OpenInPreviewTabAsync(filePath).FireAndForget();
            }
            else
            {
                VS.Documents.OpenAsync(filePath).FireAndForget();
            }
        }
    }
}
