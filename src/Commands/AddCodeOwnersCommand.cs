using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a CODEOWNERS file.
    /// </summary>
    [Command(PackageIds.AddCodeOwners)]
    internal sealed class AddCodeOwnersCommand : GitHubFileCommandBase<AddCodeOwnersCommand>
    {
        // No dialog needed - fixed file name
        protected override string DialogTitle => null;
        protected override string ErrorMessagePrefix => "Failed to create CODEOWNERS";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            return Path.Combine(targetFolder, "CODEOWNERS");
        }

        protected override string GetFileContent(string userInput)
        {
            return FileTemplates.CodeOwners;
        }
    }
}
