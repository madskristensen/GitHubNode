using EnvDTE;
using EnvDTE80;
using GitHubNode.Services;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to delete an MCP server from its configuration file.
    /// </summary>
    [Command(PackageIds.DeleteMcpServer)]
    internal sealed class DeleteMcpServerCommand : BaseCommand<DeleteMcpServerCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (McpContextMenuController.CurrentItem is not McpServerNode serverNode)
            {
                return;
            }

            string serverName = serverNode.ServerName;
            string configFilePath = serverNode.ConfigFilePath;

            bool result = await VS.MessageBox.ShowConfirmAsync(
                "Delete MCP Server",
                $"Are you sure you want to delete the MCP server '{serverName}'?\n\nThis action cannot be undone.");

            if (!result)
            {
                return;
            }

            bool success = McpConfigService.DeleteServer(configFilePath, serverName, out bool fileWasDeleted);

            if (success)
            {
                // If the config file was deleted, close it if it's open in VS
                if (fileWasDeleted)
                {
                    await CloseDocumentIfOpenAsync(configFilePath);
                }

                // Refresh the MCP root node to update the tree
                // Navigate up the parent chain to find the root node
                object current = serverNode.ParentItem;
                while (current != null)
                {
                    if (current is McpRootNode rootNode)
                    {
                        rootNode.RefreshChildren();
                        break;
                    }

                    if (current is McpNodeBase nodeBase)
                    {
                        current = nodeBase.ParentItem;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            else
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Delete Failed",
                    $"Could not delete the MCP server '{serverName}'.");
            }
        }

        /// <summary>
        /// Closes a document if it is currently open in Visual Studio.
        /// </summary>
        private static async Task CloseDocumentIfOpenAsync(string filePath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                DTE2 dte = await VS.GetServiceAsync<DTE, DTE2>();
                if (dte?.Documents == null)
                {
                    return;
                }

                foreach (Document doc in dte.Documents)
                {
                    if (string.Equals(doc.FullName, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        doc.Close(vsSaveChanges.vsSaveChangesNo);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }
    }
}
