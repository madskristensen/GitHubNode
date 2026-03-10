using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using GitHubNode.Services;
using GitHubNode.Services.Marketplace;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A dialog for selecting an MCP server to install from marketplace plugins.
    /// Matches the style of the template selection dialog.
    /// </summary>
    internal sealed class InstallMcpServerDialog : DialogWindow
    {
        private const int _dwmwaUseImmersiveDarkMode = 20;
        private const int _dwmwaCaptionColor = 35;
        private const int _dwmwaTextColor = 36;
        private const string _allMarketplacesText = "All Marketplaces";

        private readonly ComboBox _scopeComboBox;
        private readonly ComboBox _marketplaceComboBox;
        private readonly ComboBox _serverComboBox;
        private readonly RichTextBox _previewBox;
        private readonly TextBlock _statusText;
        private readonly TextBlock _targetPathText;
        private readonly Button _refreshButton;
        private string _targetConfigPath;
        private readonly string _solutionDirectory;

        private List<McpServerItem> _allServerItems;
        private List<MarketplaceInfo> _marketplaces;

        /// <summary>
        /// Gets the selected MCP server asset, or null if cancelled.
        /// </summary>
        public PluginAsset SelectedAsset { get; private set; }

        /// <summary>
        /// Gets the selected server name to install.
        /// </summary>
        public string SelectedServerName { get; private set; }

        /// <summary>
        /// Gets the selected installation scope.
        /// </summary>
        public InstallScope SelectedScope =>
            _scopeComboBox?.SelectedIndex == 1 ? InstallScope.UserProfile : InstallScope.Solution;

        public InstallMcpServerDialog(List<PluginAsset> mcpAssets, string solutionDirectory)
        {
            _solutionDirectory = solutionDirectory;
            _targetConfigPath = McpInstallService.GetTargetConfigPath(solutionDirectory);

            // Parse the actual server info from each .mcp.json file
            _allServerItems = ParseServerItems(mcpAssets);

            // Get unique marketplaces
            _marketplaces = _allServerItems
                .Select(s => s.Asset.MarketplaceId)
                .Distinct()
                .Select(id => new MarketplaceInfo { Id = id })
                .ToList();

            Title = "Install MCP Server from Marketplace";
            Width = 550;
            Height = 500;
            MinWidth = 450;
            MinHeight = 400;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;
            HasMinimizeButton = false;
            HasHelpButton = false;
            ShowInTaskbar = false;

            // Set the owner to VS main window for proper centering
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SDTE)) is EnvDTE.DTE dte)
            {
                var hwnd = (IntPtr)dte.MainWindow.HWnd;
                if (hwnd != IntPtr.Zero)
                {
                    Owner = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
                }
            }
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Apply VS theme colors
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Scope label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Scope dropdown
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Marketplace label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Marketplace dropdown
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Server label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Server dropdown
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preview label
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Preview box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Target info
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            var currentRow = 0;

            // Prompt label
            var promptLabel = new TextBlock
            {
                Text = "Select an MCP server configuration to install:",
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            promptLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(promptLabel, currentRow++);
            grid.Children.Add(promptLabel);

            // Scope label
            var scopeLabel = new TextBlock
            {
                Text = "Install to:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            scopeLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(scopeLabel, currentRow++);
            grid.Children.Add(scopeLabel);

            // Scope dropdown
            _scopeComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                IsEditable = false
            };
            _scopeComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
            _scopeComboBox.Items.Add("Solution (shared with team)");
            _scopeComboBox.Items.Add("User Profile (all solutions)");
            _scopeComboBox.SelectedIndex = 0;
            _scopeComboBox.SelectionChanged += OnScopeSelectionChanged;
            Grid.SetRow(_scopeComboBox, currentRow++);
            grid.Children.Add(_scopeComboBox);

            // Marketplace label
            var marketplaceLabel = new TextBlock
            {
                Text = "Marketplace:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            marketplaceLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(marketplaceLabel, currentRow++);
            grid.Children.Add(marketplaceLabel);

            // Marketplace dropdown
            _marketplaceComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                IsEditable = false
            };
            _marketplaceComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
            _marketplaceComboBox.Items.Add(_allMarketplacesText);
            foreach (var marketplace in _marketplaces)
            {
                _marketplaceComboBox.Items.Add(marketplace.Id);
            }
            _marketplaceComboBox.SelectedIndex = 0;
            _marketplaceComboBox.SelectionChanged += OnMarketplaceSelectionChanged;
            Grid.SetRow(_marketplaceComboBox, currentRow++);
            grid.Children.Add(_marketplaceComboBox);

            // Server label
            var serverLabel = new TextBlock
            {
                Text = "MCP Server:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            serverLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(serverLabel, currentRow++);
            grid.Children.Add(serverLabel);

            // Server dropdown
            _serverComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                IsEditable = false
            };
            _serverComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
            _serverComboBox.SelectionChanged += OnServerSelectionChanged;
            Grid.SetRow(_serverComboBox, currentRow++);
            grid.Children.Add(_serverComboBox);

            // Preview label
            var previewLabel = new TextBlock
            {
                Text = "Preview:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            previewLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(previewLabel, currentRow++);
            grid.Children.Add(previewLabel);

            // Preview box
            _previewBox = new RichTextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(4),
                BorderThickness = new Thickness(1)
            };
            _previewBox.SetResourceReference(RichTextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _previewBox.SetResourceReference(RichTextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _previewBox.SetResourceReference(RichTextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            Grid.SetRow(_previewBox, currentRow++);
            grid.Children.Add(_previewBox);

            // Target path info
            var targetPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var targetLabel = new TextBlock
            {
                Text = "Installation target:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
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

            Grid.SetRow(targetPanel, currentRow++);
            grid.Children.Add(targetPanel);

            // Status text
            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            Grid.SetRow(_statusText, currentRow++);
            grid.Children.Add(_statusText);

            // Button row with refresh on left, Install/Cancel on right
            var buttonRowGrid = new Grid();
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(buttonRowGrid, currentRow);

            // Left side - refresh button
            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            _refreshButton = new Button
            {
                Content = "\u21BB",
                Width = 16,
                Height = 20,
                Padding = new Thickness(0),
                FontSize = 12,
                ToolTip = "Refresh marketplaces (F5)",
                Margin = new Thickness(0, 0, 4, 0)
            };
            _refreshButton.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            _refreshButton.Click += OnRefreshButtonClick;
            actionPanel.Children.Add(_refreshButton);

            Grid.SetColumn(actionPanel, 0);
            buttonRowGrid.Children.Add(actionPanel);

            // Right side - Install/Cancel buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button installButton = CreateThemedButton("Install", isDefault: true);
            installButton.Margin = new Thickness(0, 0, 8, 0);
            installButton.Click += (s, e) => OnInstallClicked();
            buttonPanel.Children.Add(installButton);

            Button cancelButton = CreateThemedButton("Cancel", isCancel: true);
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);

            Grid.SetColumn(buttonPanel, 1);
            buttonRowGrid.Children.Add(buttonPanel);

            grid.Children.Add(buttonRowGrid);

            Content = grid;

            SourceInitialized += OnSourceInitialized;

            // Initialize the server list
            UpdateServerList();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                ApplyTitleBarTheme();
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private void ApplyTitleBarTheme()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (TryGetResourceColor(EnvironmentColors.ToolWindowBackgroundBrushKey, out var captionColor))
            {
                int captionColorRef = ToColorRef(captionColor);
                _ = DwmSetWindowAttribute(handle, _dwmwaCaptionColor, ref captionColorRef, sizeof(int));
            }

            if (TryGetResourceColor(EnvironmentColors.ToolWindowTextBrushKey, out var textColor))
            {
                int textColorRef = ToColorRef(textColor);
                _ = DwmSetWindowAttribute(handle, _dwmwaTextColor, ref textColorRef, sizeof(int));
            }

            var darkMode = 1;
            _ = DwmSetWindowAttribute(handle, _dwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        }

        private bool TryGetResourceColor(object key, out Color color)
        {
            if (TryFindResource(key) is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }

            color = default;
            return false;
        }

        private static int ToColorRef(Color color)
            => color.R | (color.G << 8) | (color.B << 16);

        private async void OnRefreshButtonClick(object sender, RoutedEventArgs e)
        {
            _refreshButton.IsEnabled = false;
            _statusText.Text = "Refreshing marketplaces...";

            try
            {
                // Force refresh marketplaces
                await MarketplaceService.GetAllMarketplacesAsync(forceRefresh: true, System.Threading.CancellationToken.None);

                // Reload MCP assets
                var mcpAssets = await MarketplaceService.GetAllAssetsAsync(AssetType.McpServer, System.Threading.CancellationToken.None);

                // Re-parse server items
                _allServerItems = ParseServerItems(mcpAssets);

                // Update marketplaces list
                _marketplaces = _allServerItems
                    .Select(s => s.Asset.MarketplaceId)
                    .Distinct()
                    .Select(id => new MarketplaceInfo { Id = id })
                    .ToList();

                // Rebuild the marketplace dropdown
                _marketplaceComboBox.Items.Clear();
                _marketplaceComboBox.Items.Add(_allMarketplacesText);
                foreach (var marketplace in _marketplaces)
                {
                    _marketplaceComboBox.Items.Add(marketplace.Id);
                }
                _marketplaceComboBox.SelectedIndex = 0;

                // This will trigger UpdateServerList via the selection changed event
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                _refreshButton.IsEnabled = true;
            }
        }

        private void OnMarketplaceSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateServerList();
        }

        private void OnScopeSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateTargetPath();
        }

        private void UpdateTargetPath()
        {
            if (SelectedScope == InstallScope.UserProfile)
            {
                _targetConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".mcp.json");
            }
            else
            {
                _targetConfigPath = McpInstallService.GetTargetConfigPath(_solutionDirectory);
            }

            _targetPathText.Text = _targetConfigPath ?? "No workspace configuration found";
            UpdateStatusText(_serverComboBox.Items.Count);
        }

        private void OnServerSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdateServerList()
        {
            _serverComboBox.Items.Clear();

            string selectedMarketplace = _marketplaceComboBox.SelectedItem as string;
            bool showAll = selectedMarketplace == _allMarketplacesText;

            var filteredServers = showAll
                ? _allServerItems
                : _allServerItems.Where(s => s.Asset.MarketplaceId == selectedMarketplace).ToList();

            foreach (var server in filteredServers)
            {
                string transportInfo = server.TransportType == "http" ? " (HTTP)" : " (stdio)";
                string displayText = showAll
                    ? $"{server.ServerName}{transportInfo} - {server.PluginName}"
                    : $"{server.ServerName}{transportInfo}";

                var item = new ComboBoxItem
                {
                    Content = displayText,
                    Tag = server
                };
                _serverComboBox.Items.Add(item);
            }

            if (_serverComboBox.Items.Count > 0)
            {
                _serverComboBox.SelectedIndex = 0;
            }

            UpdateStatusText(filteredServers.Count);
        }

        private void UpdateStatusText(int count)
        {
            bool configExists = !string.IsNullOrEmpty(_targetConfigPath) && File.Exists(_targetConfigPath);
            string configNote = configExists
                ? "Server will be merged into existing configuration."
                : "A new configuration file will be created.";

            _statusText.Text = $"{count} server(s) available. {configNote}";
        }

        private void UpdatePreview()
        {
            _previewBox.Document.Blocks.Clear();

            if (_serverComboBox.SelectedItem is ComboBoxItem item && item.Tag is McpServerItem serverItem)
            {
                // Show just this server's configuration
                string jsonContent = serverItem.ServerConfigJson ?? "// No configuration available";

                var paragraph = new Paragraph(new Run(jsonContent))
                {
                    Margin = new Thickness(0)
                };
                _previewBox.Document.Blocks.Add(paragraph);
            }
        }

        private void OnInstallClicked()
        {
            if (_serverComboBox.SelectedItem is ComboBoxItem item && item.Tag is McpServerItem serverItem)
            {
                SelectedAsset = serverItem.Asset;
                SelectedServerName = serverItem.ServerName;
                DialogResult = true;
                Close();
            }
        }

        /// <summary>
        /// Parses the .mcp.json files to extract actual server names and info.
        /// Skips files that are not valid JSON (e.g., symlink pointer files).
        /// </summary>
        private static List<McpServerItem> ParseServerItems(List<PluginAsset> mcpAssets)
        {
            var items = new List<McpServerItem>();

            foreach (PluginAsset asset in mcpAssets)
            {
                try
                {
                    if (!File.Exists(asset.LocalPath))
                    {
                        continue;
                    }

                    string json = File.ReadAllText(asset.LocalPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Try "mcpServers" first (marketplace format), then "servers" (VS format)
                    System.Text.Json.JsonElement serversElement;
                    if (root.TryGetProperty("mcpServers", out serversElement) && serversElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        // Found mcpServers
                    }
                    else if (root.TryGetProperty("servers", out serversElement) && serversElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        // Found servers
                    }
                    else
                    {
                        continue;
                    }

                    // Add each server defined in the config file
                    foreach (var serverProp in serversElement.EnumerateObject())
                    {
                        string serverName = serverProp.Name;
                        string transportType = GetTransportType(serverProp.Value);
                        string serverConfigJson = FormatJson(serverProp.Value.GetRawText());

                        items.Add(new McpServerItem
                        {
                            ServerName = serverName,
                            TransportType = transportType,
                            ServerConfigJson = serverConfigJson,
                            Asset = asset,
                            PluginName = asset.PluginName,
                            MarketplaceId = asset.MarketplaceId
                        });
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Skip invalid JSON files
                }
                catch (IOException)
                {
                    // Skip unreadable files
                }
            }

            return items;
        }

        private static string GetTransportType(System.Text.Json.JsonElement serverConfig)
        {
            if (serverConfig.TryGetProperty("url", out var urlElement) &&
                urlElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                string url = urlElement.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return "HTTP";
                }
            }

            return "stdio";
        }

        /// <summary>
        /// Formats raw JSON with indentation for display.
        /// </summary>
        private static string FormatJson(string rawJson)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch
            {
                return rawJson;
            }
        }

        private static string GetFallbackName(PluginAsset asset)
        {
            if (!string.IsNullOrEmpty(asset.LocalPath))
            {
                string parentFolder = Path.GetFileName(Path.GetDirectoryName(asset.LocalPath));
                if (!string.IsNullOrEmpty(parentFolder) && parentFolder != "plugins")
                {
                    return parentFolder;
                }
            }

            return asset.PluginName ?? "MCP Server";
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

        /// <summary>
        /// Represents a single MCP server entry parsed from a config file.
        /// </summary>
        private sealed class McpServerItem
        {
            public string ServerName { get; set; }
            public string TransportType { get; set; }
            public string ServerConfigJson { get; set; }
            public PluginAsset Asset { get; set; }
            public string PluginName { get; set; }
            public string MarketplaceId { get; set; }
        }

        /// <summary>
        /// Simple holder for marketplace info.
        /// </summary>
        private sealed class MarketplaceInfo
        {
            public string Id { get; set; }
        }
    }
}
