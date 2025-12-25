using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a dependabot.yml file.
    /// </summary>
    [Command(PackageIds.AddDependabot)]
    internal sealed class AddDependabotCommand : GitHubFileCommandBase<AddDependabotCommand>
    {
        // No dialog needed - fixed file name
        protected override string DialogTitle => null;
        protected override string ErrorMessagePrefix => "Failed to create dependabot.yml";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            return Path.Combine(targetFolder, "dependabot.yml");
        }

        protected override string GetFileContent(string userInput)
        {
            return FileTemplates.Dependabot;
        }
    }
}
