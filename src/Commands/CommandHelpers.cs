using System.IO;
using System.Linq;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Helper methods shared across commands.
    /// </summary>
    internal static class CommandHelpers
    {
        /// <summary>
        /// Gets a supported AI root folder path from any path within or below it.
        /// </summary>
        public static string GetGitHubFolderPath(string path)
        {
            return AiRootFolders.GetRootFolderPath(path);
        }

        /// <summary>
        /// Sanitizes a string to be used as a filename.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("", name.Select(c => invalid.Contains(c) ? '-' : c))
                .ToLowerInvariant()
                .Replace(' ', '-');
        }

        /// <summary>
        /// Ensures a folder exists within the .github folder.
        /// </summary>
        public static string EnsureFolder(string gitHubFolder, string folderName)
        {
            var folderPath = Path.Combine(gitHubFolder, folderName);
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }
    }
}
