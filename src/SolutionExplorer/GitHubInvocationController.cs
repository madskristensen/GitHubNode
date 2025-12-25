using System.Collections.Generic;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Handles double-click and Enter key invocations on GitHub file nodes.
    /// </summary>
    internal sealed class GitHubInvocationController : IInvocationController
    {
        private static GitHubInvocationController _instance;

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static GitHubInvocationController Instance => _instance ??= new GitHubInvocationController();

        private GitHubInvocationController() { }

        public bool Invoke(IEnumerable<object> items, InputSource inputSource, bool preview)
        {
            foreach (var item in items)
            {
                if (item is GitHubFileNode fileNode)
                {
                    OpenFile(fileNode, preview);
                }
            }

            return true;
        }

        private static void OpenFile(GitHubFileNode fileNode, bool preview)
        {
            if (!fileNode.FileExists)
            {
                VS.MessageBox.ShowWarning(
                    "File Not Found",
                    $"The file '{fileNode.FilePath}' no longer exists.");
                return;
            }

            if (preview)
            {
                VS.Documents.OpenInPreviewTabAsync(fileNode.FilePath).FireAndForget();
            }
            else
            {
                VS.Documents.OpenAsync(fileNode.FilePath).FireAndForget();
            }
        }
    }
}
