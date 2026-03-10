using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GitHubNode.Services;
using GitHubNode.Services.Marketplace;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A dialog for selecting an MCP server to install from marketplace plugins.
    /// </summary>
    internal sealed class InstallMcpServerDialog : DialogWindow
    {
        private readonly ListBox _serverListBox;
        private readonly List<PluginAsset> _assets;
        private readonly TextBlock _targetPathText;
        private readonly string _targetConfigPath;

        /// <summary>
        /// Gets the selected MCP server asset, or null if cancelled.
        /// </summary>
        public PluginAsset SelectedAsset { get; private set; }

        public InstallMcpServerDialog(List<PluginAsset> mcpAssets, string solutionDirectory)
        {
            _assets = mcpAssets;
            _targetConfigPath = McpInstallService.GetTargetConfigPath(solutionDirectory);

            Title = "Install MCP Server from Marketplace";
            Width = 550;
            Height = 450;
            MinWidth = 450;
            MinHeight = 350;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;
            HasMinimizeButton = false;
            HasHelpButton = false;
            ShowInTaskbar = false;

            // Set the owner to VS main window for proper centering
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SDTE)) is EnvDTE.DTE dte)
            {
                var hwnd = (System.IntPtr)dte.MainWindow.HWnd;
                if (hwnd != System.IntPtr.Zero)
                {
                    Owner = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
                }
            }
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Apply VS theme colors
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Target info
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons
            grid.Margin = new Thickness(12);

            // Prompt label
            var label = new TextBlock
            {
                Text = "Select an MCP server to install:",
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            // Server list
            _serverListBox = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                BorderThickness = new Thickness(1)
            };
            _serverListBox.SetResourceReference(ListBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _serverListBox.SetResourceReference(ListBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _serverListBox.SetResourceReference(ListBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);

            // Group assets by marketplace/plugin
            var groupedAssets = _assets
                .GroupBy(a => a.PluginName ?? "Unknown")
                .OrderBy(g => g.Key);

            foreach (var group in groupedAssets)
            {
                // Add group header
                var groupHeader = new ListBoxItem
                {
                    Content = CreateGroupHeader(group.Key),
                    IsEnabled = false,
                    Focusable = false
                };
                _serverListBox.Items.Add(groupHeader);

                // Add items in the group
                foreach (PluginAsset asset in group)
                {
                    var itemPanel = CreateServerItem(asset);
                    var listBoxItem = new ListBoxItem { Content = itemPanel, Tag = asset };
                    _serverListBox.Items.Add(listBoxItem);
                }
            }

            // Select first selectable item
            foreach (var item in _serverListBox.Items)
            {
                if (item is ListBoxItem lbi && lbi.IsEnabled)
                {
                    _serverListBox.SelectedItem = lbi;
                    break;
                }
            }

            // Handle double-click
            _serverListBox.MouseDoubleClick += (s, e) =>
            {
                if (_serverListBox.SelectedItem is ListBoxItem item && item.Tag is PluginAsset)
                {
                    OnOkClicked();
                }
            };

            Grid.SetRow(_serverListBox, 1);
            grid.Children.Add(_serverListBox);

            // Target path info
            var targetPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var targetLabel = new TextBlock
            {
                Text = "Installation target:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            targetLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            targetPanel.Children.Add(targetLabel);

            _targetPathText = new TextBlock
            {
                Text = _targetConfigPath ?? "No workspace configuration found",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            };
            _targetPathText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            targetPanel.Children.Add(_targetPathText);

            bool configExists = !string.IsNullOrEmpty(_targetConfigPath) && File.Exists(_targetConfigPath);
            var noteText = new TextBlock
            {
                Text = configExists
                    ? "Servers will be merged into the existing configuration."
                    : "A new configuration file will be created.",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0)
            };
            noteText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            targetPanel.Children.Add(noteText);

            Grid.SetRow(targetPanel, 2);
            grid.Children.Add(targetPanel);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button installButton = CreateThemedButton("Install", isDefault: true);
            installButton.Margin = new Thickness(0, 0, 8, 0);
            installButton.Click += (s, e) => OnOkClicked();
            buttonPanel.Children.Add(installButton);

            Button cancelButton = CreateThemedButton("Cancel", isCancel: true);
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private static StackPanel CreateGroupHeader(string groupName)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };

            var text = new TextBlock
            {
                Text = groupName,
                FontWeight = FontWeights.Bold,
                FontSize = 12
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            panel.Children.Add(text);

            return panel;
        }

        private static StackPanel CreateServerItem(PluginAsset asset)
        {
            var itemPanel = new StackPanel { Margin = new Thickness(16, 4, 4, 4) };

            // Server name (derived from file location or asset name)
            string displayName = GetDisplayName(asset);
            var nameText = new TextBlock
            {
                Text = displayName,
                FontWeight = FontWeights.SemiBold
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            itemPanel.Children.Add(nameText);

            // Description or source info
            string description = !string.IsNullOrEmpty(asset.Description)
                ? asset.Description
                : $"From: {asset.MarketplaceId}";
            var descText = new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            descText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            itemPanel.Children.Add(descText);

            return itemPanel;
        }

        private static string GetDisplayName(PluginAsset asset)
        {
            // Try to derive a meaningful name from the asset
            if (!string.IsNullOrEmpty(asset.Name) && asset.Name != "mcp.json" && asset.Name != ".mcp.json")
            {
                return asset.Name;
            }

            // Use the parent folder name
            if (!string.IsNullOrEmpty(asset.LocalPath))
            {
                string parentFolder = Path.GetFileName(Path.GetDirectoryName(asset.LocalPath));
                if (!string.IsNullOrEmpty(parentFolder) && parentFolder != "plugins")
                {
                    return parentFolder;
                }
            }

            // Fallback to plugin name
            return asset.PluginName ?? "MCP Server";
        }

        private void OnOkClicked()
        {
            if (_serverListBox.SelectedItem is ListBoxItem selectedItem &&
                selectedItem.Tag is PluginAsset asset)
            {
                SelectedAsset = asset;
                DialogResult = true;
                Close();
            }
        }

        private static Button CreateThemedButton(string content, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 75,
                Height = 23,
                Padding = new Thickness(8, 0, 8, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            button.SetResourceReference(Button.StyleProperty, VsResourceKeys.ButtonStyleKey);
            return button;
        }
    }
}
