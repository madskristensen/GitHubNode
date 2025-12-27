using System.IO;
using GitHubNode.Services;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add an agent skill.
    /// </summary>
    [Command(PackageIds.AddSkill)]
    internal sealed class AddSkillCommand : GitHubFileCommandBase<AddSkillCommand>
    {
        protected override string DialogTitle => "New Agent Skill";
        protected override string DialogPrompt => "Select a template or create a custom skill:";
        protected override string DialogDefaultValue => "my-skill";
        protected override string ErrorMessagePrefix => "Failed to create skill";
        protected override TemplateType? TemplateType => Services.TemplateType.Skill;

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
