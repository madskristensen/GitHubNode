global using System;
global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using Task = System.Threading.Tasks.Task;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace GitHubNode
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.GitHubNodeString)]
    public sealed class GitHubNodePackage : ToolkitPackage
    {
        /// <summary>
        /// Gets the singleton instance of the package.
        /// </summary>
        public static GitHubNodePackage Instance { get; private set; }

        /// <summary>
        /// Gets the dialog settings dictionary for persisting UI preferences.
        /// </summary>
        public Dictionary<string, double> DialogSettings { get; } = [];

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            await this.RegisterCommandsAsync();
        }
    }
}