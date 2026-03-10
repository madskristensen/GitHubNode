using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

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
            return GetAllLocations(solutionDirectory).Where(l => l.Exists).ToList();
        }

        /// <summary>
        /// Parses server information from an MCP configuration file, including transport type.
        /// </summary>
        /// <param name="filePath">The path to the MCP configuration file.</param>
        /// <returns>Dictionary mapping server names to transport types (stdio or http).</returns>
        public static Dictionary<string, string> ParseServerInfo(string filePath)
        {
            var serverInfo = new Dictionary<string, string>();

            if (!File.Exists(filePath))
            {
                return serverInfo;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                JsonElement? servers = GetServersObject(json);
                if (servers == null)
                {
                    return serverInfo;
                }

                foreach (JsonProperty serverEntry in servers.Value.EnumerateObject())
                {
                    string serverName = serverEntry.Name;
                    string transportType = GetTransportType(serverEntry.Value);
                    serverInfo[serverName] = transportType;
                }
            }
            catch (JsonException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.ParseServerInfo failed for '{filePath}': {ex}");
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.ParseServerInfo failed for '{filePath}': {ex}");
            }

            return serverInfo;
        }

        /// <summary>
        /// Parses server names from an MCP configuration file.
        /// </summary>
        /// <param name="filePath">The path to the MCP configuration file.</param>
        /// <returns>List of server names, or empty list if parsing fails.</returns>
        public static List<string> ParseServerNames(string filePath)
        {
            return new List<string>(ParseServerInfo(filePath).Keys);
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
            catch (ArgumentException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.CreateConfigFile failed for '{filePath}': {ex}");
                return false;
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.CreateConfigFile failed for '{filePath}': {ex}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.CreateConfigFile failed for '{filePath}': {ex}");
                return false;
            }
        }

        /// <summary>
        /// Parses the JSON and returns the servers object (handling both "servers" and "mcpServers" keys).
        /// </summary>
        private static JsonElement? GetServersObject(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                // Check for VS format ("servers") first, then marketplace format ("mcpServers")
                if (root.TryGetProperty("servers", out JsonElement servers) && servers.ValueKind == JsonValueKind.Object)
                {
                    return servers.Clone();
                }

                if (root.TryGetProperty("mcpServers", out JsonElement mcpServers) && mcpServers.ValueKind == JsonValueKind.Object)
                {
                    return mcpServers.Clone();
                }
            }
            catch (JsonException)
            {
                // Invalid JSON, return null
            }

            return null;
        }

        private static string GetTransportType(JsonElement serverConfig)
        {
            if (serverConfig.TryGetProperty("url", out JsonElement urlElement) &&
                urlElement.ValueKind == JsonValueKind.String)
            {
                string url = urlElement.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return "http";
                }
            }

            return "stdio";
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

        /// <summary>
        /// Deletes an MCP server from a configuration file.
        /// If the server is the last one in a workspace file (not user profile), the file is deleted.
        /// </summary>
        /// <param name="configFilePath">The path to the MCP configuration file.</param>
        /// <param name="serverName">The name of the server to delete.</param>
        /// <param name="fileWasDeleted">Set to true if the config file was deleted (last server in workspace file).</param>
        /// <returns>True if the operation succeeded, false otherwise.</returns>
        public static bool DeleteServer(string configFilePath, string serverName, out bool fileWasDeleted)
        {
            fileWasDeleted = false;

            if (string.IsNullOrEmpty(configFilePath) || !File.Exists(configFilePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(configFilePath);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                // Determine which key holds the servers
                string serversKey = null;
                if (root.TryGetProperty("servers", out JsonElement servers) && servers.ValueKind == JsonValueKind.Object)
                {
                    serversKey = "servers";
                }
                else if (root.TryGetProperty("mcpServers", out JsonElement mcpServers) && mcpServers.ValueKind == JsonValueKind.Object)
                {
                    serversKey = "mcpServers";
                    servers = mcpServers;
                }

                if (serversKey == null)
                {
                    return false;
                }

                // Count servers and check if this server exists
                int serverCount = 0;
                bool serverExists = false;
                foreach (JsonProperty serverEntry in servers.EnumerateObject())
                {
                    serverCount++;
                    if (string.Equals(serverEntry.Name, serverName, StringComparison.Ordinal))
                    {
                        serverExists = true;
                    }
                }

                if (!serverExists)
                {
                    return false;
                }

                // If this is the last server and it's a workspace file, delete the file
                bool isUserProfile = IsUserProfileConfig(configFilePath);
                if (serverCount == 1 && !isUserProfile)
                {
                    File.Delete(configFilePath);
                    fileWasDeleted = true;

                    // Delete the parent directory if empty (e.g., .vscode or .cursor folder)
                    string parentDir = Path.GetDirectoryName(configFilePath);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(parentDir).Any())
                            {
                                Directory.Delete(parentDir);
                            }
                        }
                        catch
                        {
                            // Ignore errors when cleaning up empty directories
                        }
                    }

                    return true;
                }

                // Otherwise, remove just the server entry and rewrite the file
                return RemoveServerFromConfig(configFilePath, json, serversKey, serverName);
            }
            catch (JsonException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.DeleteServer failed for '{configFilePath}': {ex}");
                return false;
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.DeleteServer failed for '{configFilePath}': {ex}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpConfigService.DeleteServer failed for '{configFilePath}': {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the config file is the user profile config.
        /// </summary>
        private static bool IsUserProfileConfig(string configFilePath)
        {
            string userProfilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mcp.json");
            return string.Equals(configFilePath, userProfilePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes a server entry from the config file and rewrites it.
        /// </summary>
        private static bool RemoveServerFromConfig(string configFilePath, string originalJson, string serversKey, string serverName)
        {
            using JsonDocument doc = JsonDocument.Parse(originalJson);
            JsonElement root = doc.RootElement;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();

                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name == serversKey)
                    {
                        writer.WritePropertyName(serversKey);
                        writer.WriteStartObject();

                        foreach (JsonProperty server in property.Value.EnumerateObject())
                        {
                            if (!string.Equals(server.Name, serverName, StringComparison.Ordinal))
                            {
                                writer.WritePropertyName(server.Name);
                                server.Value.WriteTo(writer);
                            }
                        }

                        writer.WriteEndObject();
                    }
                    else
                    {
                        writer.WritePropertyName(property.Name);
                        property.Value.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            string newJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            File.WriteAllText(configFilePath, newJson);
            return true;
        }
    }
}
