using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add an agent skill.
    /// </summary>
    [Command(PackageIds.AddSkill)]
    internal sealed class AddSkillCommand : GitHubFileCommandBase<AddSkillCommand>
    {
        protected override string DialogTitle => "New Agent Skill";
        protected override string DialogPrompt => "Enter the skill name:";
        protected override string DialogDefaultValue => "my-skill";
        protected override string ErrorMessagePrefix => "Failed to create skill";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            var skillsFolder = Path.Combine(targetFolder, "Skills");
            var skillFolder = Path.Combine(skillsFolder, CommandHelpers.SanitizeFileName(userInput));
            return Path.Combine(skillFolder, "skill.md");
        }

        protected override string GetFileContent(string userInput)
        {
            return string.Format(FileTemplates.AgentSkill, userInput);
        }
    }
}
