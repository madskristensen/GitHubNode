using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    internal sealed class AiRootFolderDefinition
    {
        public AiRootFolderDefinition(string folderName, string displayName, ImageMoniker iconMoniker, bool alwaysVisible)
        {
            FolderName = folderName;
            DisplayName = displayName;
            IconMoniker = iconMoniker;
            AlwaysVisible = alwaysVisible;
        }

        public string FolderName { get; }
        public string DisplayName { get; }
        public ImageMoniker IconMoniker { get; }
        public bool AlwaysVisible { get; }
    }

    internal static class AiRootFolders
    {
        private static readonly ImageMoniker _claudeIcon = new()
        {
            Guid = new System.Guid("48dc1369-76d5-448f-b1fd-85333d8ff6ce"),
            Id = 0,
        };

        public static readonly AiRootFolderDefinition GitHub = new(
            folderName: ".github",
            displayName: "GitHub",
            iconMoniker: KnownMonikers.GitHub,
            alwaysVisible: true);

        public static readonly AiRootFolderDefinition Claude = new(
            folderName: ".claude",
            displayName: "Claude",
            iconMoniker: _claudeIcon,
            alwaysVisible: false);

        public static readonly AiRootFolderDefinition Agents = new(
            folderName: ".agents",
            displayName: "Agents",
            iconMoniker: KnownMonikers.Spy,
            alwaysVisible: false);

        public static readonly IReadOnlyList<AiRootFolderDefinition> All =
        [
            GitHub,
            Claude,
            Agents,
        ];

        public static string GetRootFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (TryGetRootFolderName(path, out string matchedRootName))
            {
                return matchedRootName == Path.GetFileName(path)
                    ? path
                    : ExtractRootFolderPath(path, matchedRootName);
            }

            foreach (AiRootFolderDefinition folder in All)
            {
                string candidate = Path.Combine(path, folder.FolderName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool TryGetRootFolderName(string path, out string matchedRootName)
        {
            string fileName = Path.GetFileName(path);
            matchedRootName = All
                .Select(folder => folder.FolderName)
                .FirstOrDefault(name => name.Equals(fileName, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(matchedRootName))
            {
                return true;
            }

            string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string[] parts = normalizedPath.Split(Path.DirectorySeparatorChar);
            matchedRootName = parts.FirstOrDefault(part =>
                All.Any(folder => folder.FolderName.Equals(part, System.StringComparison.OrdinalIgnoreCase)));

            return !string.IsNullOrEmpty(matchedRootName);
        }

        private static string ExtractRootFolderPath(string path, string rootFolderName)
        {
            string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string[] parts = normalizedPath.Split(Path.DirectorySeparatorChar);
            int index = System.Array.FindIndex(parts, part => rootFolderName.Equals(part, System.StringComparison.OrdinalIgnoreCase));

            return index >= 0
                ? string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(index + 1))
                : null;
        }
    }
}
