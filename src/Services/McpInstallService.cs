using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GitHubNode.Services
{
    /// <summary>
    /// Result of an MCP server installation operation.
    /// </summary>
    internal sealed class McpInstallResult
    {
        /// <summary>
        /// Gets or sets whether the installation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets a message describing the result.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the path to the target configuration file.
        /// </summary>
        public string TargetFilePath { get; set; }

        /// <summary>
        /// Gets or sets the names of servers that were installed.
        /// </summary>
        public List<string> InstalledServers { get; set; } = [];

        /// <summary>
        /// Gets or sets the names of servers that were skipped due to conflicts.
        /// </summary>
        public List<string> SkippedServers { get; set; } = [];
    }

    /// <summary>
    /// Service for installing MCP servers from marketplace plugins.
    /// </summary>
    internal static class McpInstallService
    {
        /// <summary>
        /// Installs a specific MCP server from a marketplace .mcp.json file into the workspace.
        /// </summary>
        /// <param name="sourceFilePath">Path to the marketplace .mcp.json file.</param>
        /// <param name="serverName">The name of the specific server to install, or null to install all.</param>
        /// <param name="solutionDirectory">The solution directory path.</param>
        /// <returns>Installation result.</returns>
        public static McpInstallResult InstallFromMarketplace(string sourceFilePath, string serverName, string solutionDirectory)
        {
            string targetPath = GetTargetConfigPath(solutionDirectory);
            return InstallFromMarketplace(sourceFilePath, serverName, solutionDirectory, targetPath);
        }

        /// <summary>
        /// Installs a specific MCP server from a marketplace .mcp.json file to a specified target path.
        /// </summary>
        /// <param name="sourceFilePath">Path to the marketplace .mcp.json file.</param>
        /// <param name="serverName">The name of the specific server to install, or null to install all.</param>
        /// <param name="solutionDirectory">The solution directory path (used as fallback for target path).</param>
        /// <param name="targetConfigPath">The explicit target configuration file path.</param>
        /// <returns>Installation result.</returns>
        public static McpInstallResult InstallFromMarketplace(string sourceFilePath, string serverName, string solutionDirectory, string targetConfigPath)
        {
            var result = new McpInstallResult();

            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                result.Message = "Source MCP configuration file not found.";
                return result;
            }

            if (string.IsNullOrEmpty(targetConfigPath))
            {
                result.Message = "No target configuration path specified.";
                return result;
            }

            try
            {
                result.TargetFilePath = targetConfigPath;

                // Parse the source marketplace MCP config
                var allSourceServers = ParseMarketplaceMcpConfig(sourceFilePath);
                if (allSourceServers == null || allSourceServers.Count == 0)
                {
                    result.Message = "No MCP servers found in the source configuration.";
                    return result;
                }

                // Filter to just the selected server if specified
                var sourceServers = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(serverName))
                {
                    if (allSourceServers.TryGetValue(serverName, out string serverConfigJson))
                    {
                        sourceServers[serverName] = serverConfigJson;
                    }
                    else
                    {
                        result.Message = $"Server '{serverName}' not found in the source configuration.";
                        return result;
                    }
                }
                else
                {
                    sourceServers = allSourceServers;
                }

                // Get existing server names from target
                var existingServerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(targetConfigPath))
                {
                    var existingNames = McpConfigService.ParseServerNames(targetConfigPath);
                    foreach (var name in existingNames)
                    {
                        existingServerNames.Add(name);
                    }
                }

                // Determine which servers to install vs skip
                foreach (var kvp in sourceServers)
                {
                    if (existingServerNames.Contains(kvp.Key))
                    {
                        result.SkippedServers.Add(kvp.Key);
                    }
                    else
                    {
                        result.InstalledServers.Add(kvp.Key);
                    }
                }

                if (result.InstalledServers.Count == 0)
                {
                    result.Message = result.SkippedServers.Count > 0
                        ? $"Server already exists in the configuration: {string.Join(", ", result.SkippedServers)}"
                        : "No servers to install.";
                    return result;
                }

                // Perform the merge and save
                MergeAndSaveConfig(targetConfigPath, sourceServers, result.InstalledServers);

                result.Success = true;
                result.Message = BuildSuccessMessage(result);
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpInstallService.InstallFromMarketplace failed: {ex}");
                result.Message = $"Installation failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Gets the target configuration file path for the workspace.
        /// Returns the first existing workspace config, or defaults to solution root .mcp.json.
        /// </summary>
        /// <param name="solutionDirectory">The solution directory path.</param>
        /// <returns>The target configuration file path.</returns>
        public static string GetTargetConfigPath(string solutionDirectory)
        {
            if (string.IsNullOrEmpty(solutionDirectory))
            {
                return null;
            }

            // Get all workspace locations (skip user profile - index 0)
            List<McpConfigLocation> locations = McpConfigService.GetAllLocations(solutionDirectory);

            // Find the first existing workspace config
            for (int i = 1; i < locations.Count; i++)
            {
                if (locations[i].Exists)
                {
                    return locations[i].FilePath;
                }
            }

            // Default to solution root .mcp.json
            return Path.Combine(solutionDirectory, ".mcp.json");
        }

        /// <summary>
        /// Parses an MCP config file from a marketplace plugin.
        /// Handles both "mcpServers" (marketplace format) and "servers" (VS format).
        /// Returns the raw JSON text for each server.
        /// </summary>
        private static Dictionary<string, string> ParseMarketplaceMcpConfig(string filePath)
        {
            var servers = new Dictionary<string, string>();

            try
            {
                string json = File.ReadAllText(filePath);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                // Try "mcpServers" first (marketplace format), then "servers" (VS format)
                JsonElement? serversElement = null;
                if (root.TryGetProperty("mcpServers", out JsonElement mcpServers) && mcpServers.ValueKind == JsonValueKind.Object)
                {
                    serversElement = mcpServers;
                }
                else if (root.TryGetProperty("servers", out JsonElement vsServers) && vsServers.ValueKind == JsonValueKind.Object)
                {
                    serversElement = vsServers;
                }

                if (serversElement == null)
                {
                    return servers;
                }

                foreach (JsonProperty prop in serversElement.Value.EnumerateObject())
                {
                    // Store the raw JSON text for the server config
                    servers[prop.Name] = prop.Value.GetRawText();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"McpInstallService.ParseMarketplaceMcpConfig failed for '{filePath}': {ex}");
            }

            return servers;
        }

        /// <summary>
        /// Merges source servers into the target config and saves it.
        /// sourceServers contains server name -> raw JSON string mappings.
        /// </summary>
        private static void MergeAndSaveConfig(string targetPath, Dictionary<string, string> sourceServers, List<string> serversToInstall)
        {
            // Read the existing file or create default content
            string existingJson;
            if (File.Exists(targetPath))
            {
                existingJson = File.ReadAllText(targetPath);
                }
                else
                {
                    existingJson = McpConfigService.GetDefaultContent();
                }

                // Build the new servers JSON to insert
                var newServersJson = new StringBuilder();
                bool first = true;

                foreach (string serverName in serversToInstall)
                {
                    if (sourceServers.TryGetValue(serverName, out string serverConfigJson))
                    {
                        if (!first)
                        {
                            newServersJson.Append(",");
                        }
                        first = false;

                        // Format as "serverName": { config }
                        newServersJson.AppendLine();
                        newServersJson.Append($"    \"{EscapeJsonString(serverName)}\": {serverConfigJson}");
                    }
                }

                // Insert the new servers into the JSON
                string updatedJson = InsertServersIntoJson(existingJson, newServersJson.ToString());

                // Ensure directory exists
                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(targetPath, updatedJson);
        }

        /// <summary>
        /// Escapes a string for JSON.
        /// </summary>
        private static string EscapeJsonString(string str)
        {
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// Inserts new server configurations into existing JSON.
        /// </summary>
        private static string InsertServersIntoJson(string existingJson, string newServersJson)
        {
            // Find the "servers" object and insert before its closing brace
            int serversIndex = existingJson.IndexOf("\"servers\"", StringComparison.OrdinalIgnoreCase);
            if (serversIndex == -1)
            {
                // No servers section - need to add one
                // Find the closing brace of the root object
                int lastBrace = existingJson.LastIndexOf('}');
                if (lastBrace > 0)
                {
                    string before = existingJson.Substring(0, lastBrace).TrimEnd();
                    if (!before.EndsWith(","))
                    {
                        before += ",";
                    }
                    return before + "\n  \"servers\": {" + newServersJson + "\n  }\n}";
                }
                return existingJson;
            }

            // Find the opening brace of servers object
            int openBrace = existingJson.IndexOf('{', serversIndex);
            if (openBrace == -1)
            {
                return existingJson;
            }

            // Find the matching closing brace
            int closeBrace = FindMatchingBrace(existingJson, openBrace);
            if (closeBrace == -1)
            {
                return existingJson;
            }

            // Check if servers object is empty
            string serversContent = existingJson.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
            bool isEmpty = string.IsNullOrWhiteSpace(serversContent);

            // Insert new servers
            if (isEmpty)
            {
                // Empty servers object - just insert
                return existingJson.Substring(0, closeBrace) + newServersJson + "\n  " + existingJson.Substring(closeBrace);
            }
            else
            {
                // Has existing servers - add comma and insert
                return existingJson.Substring(0, closeBrace) + "," + newServersJson + "\n  " + existingJson.Substring(closeBrace);
            }
        }

        /// <summary>
        /// Finds the matching closing brace for an opening brace.
        /// </summary>
        private static int FindMatchingBrace(string json, int openBraceIndex)
        {
            int depth = 1;
            bool inString = false;
            bool escape = false;

            for (int i = openBraceIndex + 1; i < json.Length; i++)
            {
                char c = json[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Builds a success message describing what was installed.
        /// </summary>
        private static string BuildSuccessMessage(McpInstallResult result)
        {
            var sb = new StringBuilder();
            sb.Append($"Installed {result.InstalledServers.Count} MCP server(s): {string.Join(", ", result.InstalledServers)}");

            if (result.SkippedServers.Count > 0)
            {
                sb.Append($"\nSkipped {result.SkippedServers.Count} existing server(s): {string.Join(", ", result.SkippedServers)}");
            }

            return sb.ToString();
        }
    }
}
