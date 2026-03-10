using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

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
        /// Installs MCP servers from a marketplace .mcp.json file into the workspace.
        /// Uses the first existing workspace config location, or creates one at the solution root.
        /// </summary>
        /// <param name="sourceFilePath">Path to the marketplace .mcp.json file.</param>
        /// <param name="solutionDirectory">The solution directory path.</param>
        /// <returns>Installation result.</returns>
        public static McpInstallResult InstallFromMarketplace(string sourceFilePath, string solutionDirectory)
        {
            var result = new McpInstallResult();

            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                result.Message = "Source MCP configuration file not found.";
                return result;
            }

            if (string.IsNullOrEmpty(solutionDirectory))
            {
                result.Message = "No solution is open. Please open a solution to install MCP servers.";
                return result;
            }

            try
            {
                // Find the target config file (existing workspace config or solution root)
                string targetPath = GetTargetConfigPath(solutionDirectory);
                result.TargetFilePath = targetPath;

                // Parse the source marketplace MCP config
                var sourceServers = ParseMarketplaceMcpConfig(sourceFilePath);
                if (sourceServers == null || sourceServers.Count == 0)
                {
                    result.Message = "No MCP servers found in the source configuration.";
                    return result;
                }

                // Load or create the target config
                Dictionary<string, object> targetConfig = LoadOrCreateConfig(targetPath);

                // Get existing servers from target
                var existingServerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (targetConfig.TryGetValue("servers", out object serversObj) && serversObj is IDictionary existingServers)
                {
                    foreach (var key in existingServers.Keys)
                    {
                        if (key is string serverName)
                        {
                            existingServerNames.Add(serverName);
                        }
                    }
                }

                // Merge servers
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
                        ? $"All servers already exist in the configuration: {string.Join(", ", result.SkippedServers)}"
                        : "No servers to install.";
                    return result;
                }

                // Perform the merge and save
                MergeAndSaveConfig(targetPath, sourceServers, result.InstalledServers);

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
        /// </summary>
        private static Dictionary<string, object> ParseMarketplaceMcpConfig(string filePath)
        {
            var servers = new Dictionary<string, object>();

            try
            {
                string json = File.ReadAllText(filePath);
                object deserialized = DeserializeJson(json);

                if (deserialized is not IDictionary root)
                {
                    return servers;
                }

                // Try "mcpServers" first (marketplace format), then "servers" (VS format)
                IDictionary serversDict = null;
                if (root.Contains("mcpServers") && root["mcpServers"] is IDictionary mcpServers)
                {
                    serversDict = mcpServers;
                }
                else if (root.Contains("servers") && root["servers"] is IDictionary vsServers)
                {
                    serversDict = vsServers;
                }

                if (serversDict == null)
                {
                    return servers;
                }

                foreach (DictionaryEntry entry in serversDict)
                {
                    if (entry.Key is string serverName)
                    {
                        servers[serverName] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"McpInstallService.ParseMarketplaceMcpConfig failed for '{filePath}': {ex}");
            }

            return servers;
        }

        /// <summary>
        /// Loads an existing config file or creates a new config structure.
        /// </summary>
        private static Dictionary<string, object> LoadOrCreateConfig(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    object deserialized = DeserializeJson(json);
                    if (deserialized is IDictionary dict)
                    {
                        var result = new Dictionary<string, object>();
                        foreach (DictionaryEntry entry in dict)
                        {
                            if (entry.Key is string key)
                            {
                                result[key] = entry.Value;
                            }
                        }
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"McpInstallService.LoadOrCreateConfig failed for '{filePath}': {ex}");
                }
            }

            // Return new empty config structure
            return new Dictionary<string, object>
            {
                ["inputs"] = new object[0],
                ["servers"] = new Dictionary<string, object>()
            };
        }

        /// <summary>
        /// Merges source servers into the target config and saves it.
        /// </summary>
        private static void MergeAndSaveConfig(string targetPath, Dictionary<string, object> sourceServers, List<string> serversToInstall)
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
                if (sourceServers.TryGetValue(serverName, out object serverConfig))
                {
                    if (!first)
                    {
                        newServersJson.Append(",");
                    }
                    first = false;

                    string serverJson = SerializeServerConfig(serverName, serverConfig);
                    newServersJson.Append(serverJson);
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
        /// Serializes a server configuration to JSON format.
        /// </summary>
        private static string SerializeServerConfig(string serverName, object config)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.Append($"    \"{EscapeJsonString(serverName)}\": ");

            if (config is IDictionary dict)
            {
                sb.Append("{");
                bool first = true;

                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key is not string key)
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(",");
                    }
                    first = false;

                    sb.AppendLine();
                    sb.Append($"      \"{EscapeJsonString(key)}\": ");
                    sb.Append(SerializeValue(entry.Value));
                }

                sb.AppendLine();
                sb.Append("    }");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Serializes a value to JSON.
        /// </summary>
        private static string SerializeValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is string str)
            {
                return $"\"{EscapeJsonString(str)}\"";
            }

            if (value is bool b)
            {
                return b ? "true" : "false";
            }

            if (value is int || value is long || value is double || value is float || value is decimal)
            {
                return value.ToString();
            }

            if (value is IList list)
            {
                var items = new List<string>();
                foreach (var item in list)
                {
                    items.Add(SerializeValue(item));
                }
                return "[" + string.Join(", ", items) + "]";
            }

            if (value is IDictionary dict)
            {
                var entries = new List<string>();
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key is string key)
                    {
                        entries.Add($"\"{EscapeJsonString(key)}\": {SerializeValue(entry.Value)}");
                    }
                }
                return "{" + string.Join(", ", entries) + "}";
            }

            return $"\"{EscapeJsonString(value.ToString())}\"";
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

        private static object DeserializeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            Type serializerType = Type.GetType("System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions", throwOnError: false);
            if (serializerType == null)
            {
                throw new InvalidOperationException("System.Web.Extensions is required for JSON parsing.");
            }

            object serializer = Activator.CreateInstance(serializerType);
            MethodInfo deserializeMethod = serializerType.GetMethod("DeserializeObject", [typeof(string)]);
            if (deserializeMethod == null)
            {
                throw new InvalidOperationException("JavaScriptSerializer.DeserializeObject method was not found.");
            }

            return deserializeMethod.Invoke(serializer, [json]);
        }
    }
}
