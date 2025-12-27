using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a FUNDING.yml file.
    /// </summary>
    [Command(PackageIds.AddFunding)]
    internal sealed class AddFundingCommand : GitHubFileCommandBase<AddFundingCommand>
    {
        // No dialog needed - fixed file name
        protected override string DialogTitle => null;
        protected override string ErrorMessagePrefix => "Failed to create FUNDING.yml";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            return Path.Combine(targetFolder, "FUNDING.yml");
        }

        protected override string GetFileContent(string userInput)
        {
            return FileTemplates.Funding;
        }
    }
}
