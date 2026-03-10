namespace GitHubNode.Services
{
    /// <summary>
    /// Defines the installation scope for Copilot assets and MCP servers.
    /// </summary>
    internal enum InstallScope
    {
        /// <summary>
        /// Install to the solution's .github folder or .mcp.json.
        /// Assets are shared with the team via source control.
        /// </summary>
        Solution,

        /// <summary>
        /// Install to the user profile folder (%USERPROFILE%\.github or %USERPROFILE%\.mcp.json).
        /// Assets are available across all solutions for this user.
        /// </summary>
        UserProfile
    }
}
