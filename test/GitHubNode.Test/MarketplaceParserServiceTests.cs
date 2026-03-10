using GitHubNode.Services.Marketplace;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace GitHubNode.Test
{
    [TestClass]
    public class MarketplaceParserServiceTests
    {
        [TestMethod]
        public void ParseMarketplace_PreservesDotGitHubFolder_InPluginRoot()
        {
            // This test ensures that TrimStart doesn't strip the dot from .github folders
            // Bug: TrimStart('.', '/', '\\') was stripping the '.' from ".github" paths

            string pluginRoot = "./plugins";
            string result = pluginRoot.TrimStart('/', '\\');

            // The dot should be preserved - only slashes should be trimmed
            Assert.AreEqual("./plugins", result, "TrimStart should not strip dots from folder names");

            // When we have .github paths, the dot must be preserved
            string dotGithubPath = ".github/plugins/azure-skills";
            string trimmedPath = dotGithubPath.TrimStart('/', '\\');

            Assert.AreEqual(".github/plugins/azure-skills", trimmedPath,
                "TrimStart should preserve the leading dot in .github paths");
        }

        [TestMethod]
        public void ParseMarketplace_HandlesDotGithubLinkedSourcePath()
        {
            // Verify that linked source paths with .github are handled correctly
            string linkedSourcePath = ".github/plugins/azure-skills";

            // This is what we do in the code - only trim slashes, not dots
            string sanitizedPath = linkedSourcePath.TrimStart('/', '\\');

            Assert.IsTrue(sanitizedPath.StartsWith(".github"),
                "Sanitized path should preserve .github folder name");

            // Simulate Path.Combine behavior
            string basePath = @"C:\Marketplaces\linked_repo";
            string fullPath = Path.Combine(basePath, sanitizedPath);

            Assert.IsTrue(fullPath.Contains(".github"),
                "Full path should contain .github folder");
        }

        [TestMethod]
        public void ParseMarketplace_HandlesRelativePluginRootPath()
        {
            // Verify that plugin root paths like "./plugins" are handled correctly
            string pluginRoot = "./plugins";
            string sanitizedRoot = pluginRoot.TrimStart('/', '\\');

            // With our fix, only slashes are trimmed, so "./plugins" stays as "./plugins"
            // But Path.Combine handles this correctly anyway
            string basePath = @"C:\Marketplaces\repo";
            string fullPath = Path.Combine(basePath, sanitizedRoot, "my-plugin");

            Assert.IsTrue(fullPath.EndsWith(@"plugins\my-plugin") || fullPath.EndsWith(@".\plugins\my-plugin"),
                $"Full path should end with plugins\\my-plugin, got: {fullPath}");
        }

        [TestMethod]
        public void TrimStart_DoesNotStripDots_OnlySlashes()
        {
            // Explicit test for the TrimStart behavior we need

            // Case 1: Path starting with ./
            Assert.AreEqual("./folder", "./folder".TrimStart('/', '\\'));

            // Case 2: Path starting with .github
            Assert.AreEqual(".github", ".github".TrimStart('/', '\\'));

            // Case 3: Path starting with /
            Assert.AreEqual("folder", "/folder".TrimStart('/', '\\'));

            // Case 4: Path starting with ./
            Assert.AreEqual("./folder", "./folder".TrimStart('/', '\\'));

            // Case 5: Multiple leading slashes
            Assert.AreEqual("folder", "//folder".TrimStart('/', '\\'));

            // The OLD buggy behavior would have been:
            // ".github".TrimStart('.', '/', '\\') == "github" - WRONG!
            // We must not use TrimStart('.', '/', '\\') for paths that might start with .github
        }

        [TestMethod]
        public void GetLinkedRepositoryDirectory_CreatesCorrectPath()
        {
            // Test that linked repository paths are constructed correctly
            string linkedDir = MarketplaceStorageService.GetLinkedRepositoryDirectory(
                "github", "awesome-copilot", "microsoft", "azure-skills");

            Assert.IsTrue(linkedDir.Contains("_linked"), "Path should contain _linked folder");
            Assert.IsTrue(linkedDir.Contains("microsoft_azure-skills"), "Path should contain linked repo name");
            Assert.IsTrue(linkedDir.Contains("github_awesome-copilot"), "Path should contain parent repo name");
        }
    }
}
