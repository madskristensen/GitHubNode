using System.IO;
using System.Collections.Generic;
using GitHubNode.Services;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add an agent skill.
    /// </summary>
    [Command(PackageIds.AddSkill)]
    internal sealed class AddSkillCommand : GitHubFileCommandBase<AddSkillCommand>
    {
        protected override string DialogTitle => "Add Agent Skill";
        protected override string DialogPrompt => "Enter skill name:";
        protected override string DialogDefaultValue => "my-skill";
        protected override string ErrorMessagePrefix => "Failed to create skill";
        protected override TemplateType? TemplateType => Services.TemplateType.Skill;
        protected override string SubfolderName => "skills";
        protected override bool RequiresGitHubFolder => false;

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            // Skills have a nested folder structure: skills/{skillName}/skill.md
            var skillsFolder = GetSubfolderPath(targetFolder);
            var skillFolder = Path.Combine(skillsFolder, CommandHelpers.SanitizeFileName(userInput));
            return Path.Combine(skillFolder, "skill.md");
        }

        protected override string GetFileContent(string userInput)
            => string.Format(FileTemplates.AgentSkill, userInput);

        protected override void CopySupportingFiles(string sourceFilePath, string destinationDirectory)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || string.IsNullOrEmpty(destinationDirectory))
            {
                return;
            }

            var sourceDirectory = Path.GetDirectoryName(sourceFilePath);
            if (string.IsNullOrEmpty(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                return;
            }

            var sourceFileName = Path.GetFileName(sourceFilePath);
            CopyDirectoryContents(sourceDirectory, destinationDirectory, sourceFileName);
        }

        private static void CopyDirectoryContents(string sourceDir, string destinationDir, string excludeFileName)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                if (string.Equals(fileName, excludeFileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destFile = Path.Combine(destinationDir, fileName);
                if (!File.Exists(destFile))
                {
                    File.Copy(file, destFile);
                }
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(subDir);
                var destSubDir = Path.Combine(destinationDir, dirName);
                CopyDirectoryContents(subDir, destSubDir, excludeFileName: null);
            }
        }
    }
}
