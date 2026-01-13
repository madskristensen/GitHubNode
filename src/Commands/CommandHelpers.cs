using System.IO;
using System.Linq;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Helper methods shared across commands.
    /// </summary>
    internal static class CommandHelpers
    {
        /// <summary>
        /// Gets the .github folder path from any path within or below it.
        /// Returns the path even if the folder doesn't exist yet.
        /// </summary>
        public static string GetGitHubFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Check if we're already in or at .github
            if (Path.GetFileName(path).Equals(".github", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            // Check if path contains .github - extract it
            var normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var parts = normalizedPath.Split(Path.DirectorySeparatorChar);
            var index = Array.FindIndex(parts, p => p.Equals(".github", StringComparison.OrdinalIgnoreCase));
            
            if (index >= 0)
            {
                return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(index + 1));
            }

            // Check if .github exists as a child
            var gitHubPath = Path.Combine(path, ".github");
            if (Directory.Exists(gitHubPath))
            {
                return gitHubPath;
            }

            // .github doesn't exist - return the expected path based on the input
            // The caller is expected to pass a path that represents where .github should be
            return gitHubPath;
        }

        /// <summary>
        /// Gets the .github folder path, creating it if it doesn't exist.
        /// </summary>
        public static string GetOrCreateGitHubFolder(string path)
        {
            var gitHubPath = GetGitHubFolderPath(path);
            
            if (!string.IsNullOrEmpty(gitHubPath) && !Directory.Exists(gitHubPath))
            {
                Directory.CreateDirectory(gitHubPath);
            }

            return gitHubPath;
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
