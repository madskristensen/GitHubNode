namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Types of assets that can be found in a MarketplaceInfo plugin.
    /// </summary>
    internal enum AssetType
    {
        /// <summary>
        /// Custom agent definition (*.agent.md).
        /// </summary>
        Agent,

        /// <summary>
        /// Agent skill (skill.md or SKILL.md in a folder).
        /// </summary>
        Skill,

        /// <summary>
        /// Custom instructions (*.instructions.md or instructions.md).
        /// </summary>
        Instructions,

        /// <summary>
        /// MCP server configuration (mcp.json).
        /// </summary>
        McpServer,

        /// <summary>
        /// Prompt file (*.prompt.md).
        /// </summary>
        Prompt,

        /// <summary>
        /// GitHub Actions workflow (*.yml or *.yaml in a workflows/ folder).
        /// </summary>
        Workflow
    }
}
