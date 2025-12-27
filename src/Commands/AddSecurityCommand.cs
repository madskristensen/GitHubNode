using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a SECURITY.md file.
    /// </summary>
    [Command(PackageIds.AddSecurity)]
    internal sealed class AddSecurityCommand : GitHubFileCommandBase<AddSecurityCommand>
    {
        // No dialog needed - fixed file name
        protected override string DialogTitle => null;
        protected override string ErrorMessagePrefix => "Failed to create SECURITY.md";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            return Path.Combine(targetFolder, "SECURITY.md");
        }

        protected override string GetFileContent(string userInput)
        {
            return FileTemplates.Security;
        }
    }
}
