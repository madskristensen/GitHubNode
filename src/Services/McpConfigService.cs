using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace GitHubNode.Services
{
    /// <summary>
    /// Represents an MCP configuration file location with its servers.
    /// </summary>
    internal sealed class McpConfigLocation
    {
        /// <summary>
        /// Gets or sets the full path to the configuration file.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the display name for this location (e.g., "User Profile", "Solution Root").
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the description of this location's scope.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets whether this location is typically source-controlled.
        /// </summary>
        public bool IsSourceControlled { get; set; }

        /// <summary>
        /// Gets or sets whether the configuration file exists.
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// Gets or sets the list of server names defined in this configuration.
        /// </summary>
        public List<string> ServerNames { get; set; } = [];
    }

    /// <summary>
    /// Service for discovering and parsing MCP configuration files.
    /// </summary>
    internal static class McpConfigService
    {
        /// <summary>
        /// Gets all possible MCP configuration locations for a solution.
        /// </summary>
        /// <param name="solutionDirectory">The solution directory path.</param>
        /// <returns>List of all MCP configuration locations (both existing and potential).</returns>
        public static List<McpConfigLocation> GetAllLocations(string solutionDirectory)
        {
            var locations = new List<McpConfigLocation>();

            // 1. User profile: %USERPROFILE%\.mcp.json (Global)
            var userProfilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mcp.json");
            locations.Add(CreateLocation(
                userProfilePath,
                "User Profile",
                "Global - applies to all solutions for this user",
                isSourceControlled: false));

            if (!string.IsNullOrEmpty(solutionDirectory))
            {
                // 2. Solution .vs folder: <SolutionDir>\.vs\mcp.json (Solution-specific, user-specific)
                var vsPath = Path.Combine(solutionDirectory, ".vs", "mcp.json");
                locations.Add(CreateLocation(
                    vsPath,
                    "Solution User Settings",
                    "Solution-specific, user-specific (.vs folder)",
                    isSourceControlled: false));

                // 3. Solution root: <SolutionDir>\.mcp.json (Repository-wide)
                var solutionRootPath = Path.Combine(solutionDirectory, ".mcp.json");
                locations.Add(CreateLocation(
                    solutionRootPath,
                    "Solution Root",
                    "Repository-wide - shared with team (recommended)",
                    isSourceControlled: true));

                // 4. VS Code folder: <SolutionDir>\.vscode\mcp.json (VS Code compatibility)
                var vscodePath = Path.Combine(solutionDirectory, ".vscode", "mcp.json");
                locations.Add(CreateLocation(
                    vscodePath,
                    "VS Code",
                    "VS Code compatibility (.vscode folder)",
                    isSourceControlled: false));

                // 5. Cursor folder: <SolutionDir>\.cursor\mcp.json (Cursor compatibility)
                var cursorPath = Path.Combine(solutionDirectory, ".cursor", "mcp.json");
                locations.Add(CreateLocation(
                    cursorPath,
                    "Cursor",
                    "Cursor compatibility (.cursor folder)",
                    isSourceControlled: false));
            }

            return locations;
        }

        /// <summary>
        /// Gets only the MCP configuration locations that exist on disk.
        /// </summary>
        /// <param name="solutionDirectory">The solution directory path.</param>
        /// <returns>List of existing MCP configuration locations.</returns>
        public static List<McpConfigLocation> GetExistingLocations(string solutionDirectory)
        {
            var allLocations = GetAllLocations(solutionDirectory);
            var existingLocations = new List<McpConfigLocation>();

            foreach (var location in allLocations)
            {
                if (location.Exists)
                {
                    existingLocations.Add(location);
                }
            }

            return existingLocations;
        }

        /// <summary>
        /// Parses server names from an MCP configuration file.
        /// </summary>
        /// <param name="filePath">The path to the MCP configuration file.</param>
        /// <returns>List of server names, or empty list if parsing fails.</returns>
        public static List<string> ParseServerNames(string filePath)
        {
            var serverNames = new List<string>();

            if (!File.Exists(filePath))
            {
                return serverNames;
            }

            try
            {
                var json = File.ReadAllText(filePath);

                // Find the "servers" object and extract its keys
                // This is a simple regex-based approach that works for well-formed MCP configs
                var serversMatch = Regex.Match(json, @"""servers""\s*:\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);
                if (serversMatch.Success)
                {
                    var serversContent = serversMatch.Groups[1].Value;

                    // Extract server names (keys at the top level of the servers object)
                    var keyMatches = Regex.Matches(serversContent, @"""([^""]+)""\s*:");
                    foreach (Match keyMatch in keyMatches)
                    {
                        var key = keyMatch.Groups[1].Value;
                        // Skip nested properties by checking if this is a top-level key
                        var beforeKey = serversContent.Substring(0, keyMatch.Index);
                        var openBraces = beforeKey.Split('{').Length - 1;
                        var closeBraces = beforeKey.Split('}').Length - 1;
                        if (openBraces == closeBraces)
                        {
                            serverNames.Add(key);
                        }
                    }
                }
            }
            catch
            {
                // Failed to parse - return empty list
            }

            return serverNames;
        }

        /// <summary>
        /// Gets the default content for a new MCP configuration file.
        /// </summary>
        public static string GetDefaultContent()
        {
            return @"{
  ""inputs"": [],
  ""servers"": {
  }
}
";
        }

        /// <summary>
        /// Creates an MCP configuration file at the specified location.
        /// </summary>
        /// <param name="filePath">The path where the file should be created.</param>
        /// <returns>True if the file was created successfully.</returns>
        public static bool CreateConfigFile(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, GetDefaultContent());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static McpConfigLocation CreateLocation(string filePath, string displayName, string description, bool isSourceControlled)
        {
            var location = new McpConfigLocation
            {
                FilePath = filePath,
                DisplayName = displayName,
                Description = description,
                IsSourceControlled = isSourceControlled,
                Exists = File.Exists(filePath)
            };

            if (location.Exists)
            {
                location.ServerNames = ParseServerNames(filePath);
            }

            return location;
        }
    }
}
