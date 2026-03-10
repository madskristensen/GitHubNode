using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace GitHubNode.ToolWindows
{
    /// <summary>
    /// Tool window for managing agent marketplaces.
    /// </summary>
    [Guid("e8d2b1c4-5a3f-4e7b-9c1d-2f8a6b3e4d5c")]
    public class AgentMarketplaceToolWindow : ToolWindowPane
    {
        private readonly ContentControl _contentHost;
        private bool _contentInitialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentMarketplaceToolWindow"/> class.
        /// </summary>
        public AgentMarketplaceToolWindow() : base(null)
        {
            Caption = "Agent Marketplace";
            BitmapImageMoniker = KnownMonikers.Spy;

            // Use a content host that will be populated asynchronously
            _contentHost = new ContentControl();
            _contentHost.SetResourceReference(ContentControl.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            // Show a loading message initially
            var loadingText = new TextBlock
            {
                Text = "Loading...",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };
            loadingText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _contentHost.Content = loadingText;

            Content = _contentHost;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Schedule the async content creation after the tool window is created
            _ = InitializeContentAsync();
        }

        private async Task InitializeContentAsync()
        {
            if (_contentInitialized)
            {
                return;
            }

            _contentInitialized = true;

            // Switch to the UI thread and create the actual content
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _contentHost.Content = new AgentMarketplaceToolWindowControl();
        }

        /// <summary>
        /// Shows the tool window asynchronously.
        /// </summary>
        public static async Task ShowAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var package = GitHubNodePackage.Instance;
            if (package == null)
            {
                return;
            }

            var window = package.FindToolWindow(typeof(AgentMarketplaceToolWindow), 0, true);
            if (window?.Frame is IVsWindowFrame frame)
            {
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
            }
        }
    }
}
