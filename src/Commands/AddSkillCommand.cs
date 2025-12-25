using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add an agent skill.
    /// </summary>
    [Command(PackageIds.AddSkill)]
    internal sealed class AddSkillCommand : BaseCommand<AddSkillCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var basePath = GitHubContextMenuController.CurrentFolderPath;
            if (string.IsNullOrEmpty(basePath))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot determine target folder.");
                return;
            }

            var gitHubFolder = CommandHelpers.GetGitHubFolderPath(basePath);
            if (string.IsNullOrEmpty(gitHubFolder))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot find .github folder.");
                return;
            }

            // Prompt for skill name
            var nameDialog = new InputDialog(
                "New Agent Skill",
                "Enter the skill name:",
                "my-skill");

            if (nameDialog.ShowModal() != true || string.IsNullOrWhiteSpace(nameDialog.InputText))
            {
                return;
            }
            var skillName = nameDialog.InputText;

            // Create skill folder in Skills directly under .github
            var skillsFolder = Path.Combine(gitHubFolder, "Skills");
            var skillFolder = Path.Combine(skillsFolder, CommandHelpers.SanitizeFileName(skillName));
            Directory.CreateDirectory(skillFolder);

            var filePath = Path.Combine(skillFolder, "skill.md");

            if (File.Exists(filePath))
            {
                var result = await VS.MessageBox.ShowConfirmAsync(
                    "File Exists",
                    "skill.md already exists in this folder. Do you want to open it?");

                if (result)
                {
                    await VS.Documents.OpenAsync(filePath);
                }
                return;
            }

            try
            {
                var content = string.Format(FileTemplates.AgentSkill, skillName);
                File.WriteAllText(filePath, content);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create skill: {ex.Message}");
            }
        }
    }
}
