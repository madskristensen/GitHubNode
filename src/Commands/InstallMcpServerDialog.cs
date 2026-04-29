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
        private readonly TextBox _searchBox;
        private readonly ListBox _serverListBox;
        private readonly TextBlock _statusText;
        private readonly TextBlock _targetPathText;
        private readonly Button _refreshButton;
        private string _targetConfigPath;
        private readonly string _solutionDirectory;

        private List<McpServerItem> _allServerItems;
        private List<McpServerListItem> _serverListItems;
        private List<MarketplaceInfo> _marketplaces;
        private Dictionary<string, string> _marketplaceDisplayNames;
        private bool _isUpdatingServerChecks;

        /// <summary>
        /// Gets the selected MCP servers to install.
        /// </summary>
        public IReadOnlyList<McpServerSelection> SelectedServers { get; private set; } = [];

        /// <summary>
        /// Gets the selected installation scope.
        /// </summary>
        public InstallScope SelectedScope =>
            _scopeComboBox?.SelectedIndex == 1 ? InstallScope.UserProfile : InstallScope.Solution;

        public InstallMcpServerDialog(List<PluginAsset> mcpAssets, string solutionDirectory)
            : this(mcpAssets, solutionDirectory, null)
        {
        }

        public InstallMcpServerDialog(List<PluginAsset> mcpAssets, string solutionDirectory, IReadOnlyList<Services.Marketplace.MarketplaceInfo> allMarketplaces)
        {
            _solutionDirectory = solutionDirectory;
            _targetConfigPath = McpInstallService.GetTargetConfigPath(solutionDirectory);
            _marketplaceDisplayNames = BuildMarketplaceDisplayNames(allMarketplaces);

            // Parse the actual server info from each .mcp.json file
            _allServerItems = ParseServerItems(mcpAssets);
            _serverListItems = new List<McpServerListItem>();

            // Get unique marketplaces
            _marketplaces = _allServerItems
                .Select(s => s.Asset.MarketplaceId)
                .Distinct()
                .Select(id => new MarketplaceInfo { Id = id, DisplayName = ResolveMarketplaceDisplayName(id) })
                .ToList();

            Title = "Install MCP Server from Marketplace";
            Width = 550;
            Height = 650;
            MinWidth = 450;
            MinHeight = 500;
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
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Server label
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Server list
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
                _marketplaceComboBox.Items.Add(marketplace);
            }
            _marketplaceComboBox.SelectedIndex = 0;
            _marketplaceComboBox.SelectionChanged += OnMarketplaceSelectionChanged;
            Grid.SetRow(_marketplaceComboBox, currentRow++);
            grid.Children.Add(_marketplaceComboBox);

            var searchLabel = new TextBlock
            {
                Text = "Search:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            searchLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(searchLabel, currentRow++);
            grid.Children.Add(searchLabel);

            _searchBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(4, 2, 4, 2)
            };
            _searchBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _searchBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _searchBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            _searchBox.TextChanged += OnSearchTextChanged;
            Grid.SetRow(_searchBox, currentRow++);
            grid.Children.Add(_searchBox);

            // Server label
            var serverLabel = new TextBlock
            {
                Text = "MCP Server:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            serverLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(serverLabel, currentRow++);
            grid.Children.Add(serverLabel);

            // Server checklist
            _serverListBox = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                BorderThickness = new Thickness(1),
                MinHeight = 180
            };
            _serverListBox.SetResourceReference(ListBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _serverListBox.SetResourceReference(ListBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _serverListBox.SetResourceReference(ListBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            ScrollViewer.SetCanContentScroll(_serverListBox, true);
            ScrollViewer.SetVerticalScrollBarVisibility(_serverListBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(_serverListBox, ScrollBarVisibility.Disabled);
            Grid.SetRow(_serverListBox, currentRow++);
            grid.Children.Add(_serverListBox);

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
                var refreshedMarketplaces = await MarketplaceService.GetAllMarketplacesAsync(forceRefresh: true, System.Threading.CancellationToken.None);
                _marketplaceDisplayNames = BuildMarketplaceDisplayNames(refreshedMarketplaces);

                // Reload MCP assets
                var mcpAssets = await MarketplaceService.GetAllAssetsAsync(AssetType.McpServer, System.Threading.CancellationToken.None);

                // Re-parse server items
                _allServerItems = ParseServerItems(mcpAssets);

                // Update marketplaces list
                _marketplaces = _allServerItems
                    .Select(s => s.Asset.MarketplaceId)
                    .Distinct()
                    .Select(id => new MarketplaceInfo { Id = id, DisplayName = ResolveMarketplaceDisplayName(id) })
                    .ToList();

                // Rebuild the marketplace dropdown
                _marketplaceComboBox.Items.Clear();
                _marketplaceComboBox.Items.Add(_allMarketplacesText);
                foreach (var marketplace in _marketplaces)
                {
                    _marketplaceComboBox.Items.Add(marketplace);
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

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
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
            UpdateStatusText(_serverListItems.Count(item => !item.IsGroupHeader));
        }

        private void UpdateServerList()
        {
            object selectedItem = _marketplaceComboBox.SelectedItem;
            string selectedMarketplaceId = selectedItem is MarketplaceInfo mp ? mp.Id : null;
            bool showAll = !(selectedItem is MarketplaceInfo);
            string searchText = _searchBox?.Text?.Trim() ?? string.Empty;

            var filteredServers = showAll
                ? _allServerItems
                : _allServerItems.Where(s => s.Asset.MarketplaceId == selectedMarketplaceId).ToList();

            if (!string.IsNullOrEmpty(searchText))
            {
                filteredServers = filteredServers
                    .Where(server => server.ServerName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                        || (server.PluginName?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (server.Asset?.MarketplaceId?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();
            }

            var checkedServers = _serverListItems
                .Where(item => !item.IsGroupHeader && item.IsChecked && item.Server != null)
                .Select(item => item.Server)
                .ToHashSet();

            var nextItems = new List<McpServerListItem>();
            if (showAll)
            {
                var grouped = filteredServers
                    .GroupBy(server => server.Asset?.MarketplaceId ?? "unknown")
                    .OrderBy(group => ResolveMarketplaceDisplayName(group.Key), StringComparer.OrdinalIgnoreCase);

                foreach (var group in grouped)
                {
                    string groupDisplayName = ResolveMarketplaceDisplayName(group.Key);

                    var groupServers = group
                        .OrderBy(server => server.ServerName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(server => server.PluginName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var children = groupServers
                        .Select(server => new McpServerListItem
                        {
                            IsGroupHeader = false,
                            GroupKey = group.Key,
                            GroupDisplayName = groupDisplayName,
                            Server = server,
                            IsChecked = checkedServers.Contains(server)
                        })
                        .ToList();

                    nextItems.Add(new McpServerListItem
                    {
                        IsGroupHeader = true,
                        GroupKey = group.Key,
                        GroupDisplayName = groupDisplayName,
                        IsChecked = children.Count > 0 && children.All(item => item.IsChecked)
                    });
                    nextItems.AddRange(children);
                }
            }
            else
            {
                foreach (var server in filteredServers
                    .OrderBy(item => item.ServerName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PluginName, StringComparer.OrdinalIgnoreCase))
                {
                    string groupKey = server.Asset?.MarketplaceId ?? "unknown";
                    nextItems.Add(new McpServerListItem
                    {
                        IsGroupHeader = false,
                        GroupKey = groupKey,
                        GroupDisplayName = ResolveMarketplaceDisplayName(groupKey),
                        Server = server,
                        IsChecked = checkedServers.Contains(server)
                    });
                }
            }

            _serverListItems = nextItems;
            RenderServerListItems();
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

        private void RenderServerListItems()
        {
            if (_serverListBox == null)
            {
                return;
            }

            _serverListBox.Items.Clear();
            foreach (var item in _serverListItems)
            {
                var checkBox = new CheckBox
                {
                    IsChecked = item.IsChecked,
                    Tag = item,
                    Margin = item.IsGroupHeader ? new Thickness(0, 6, 0, 0) : new Thickness(16, 1, 0, 1),
                    FontWeight = item.IsGroupHeader ? FontWeights.SemiBold : FontWeights.Normal,
                    Content = item.IsGroupHeader ? item.GroupDisplayName : item.DisplayName,
                    ToolTip = item.IsGroupHeader ? null : CreateServerToolTip(item)
                };
                checkBox.SetResourceReference(CheckBox.StyleProperty, VsResourceKeys.CheckBoxStyleKey);
                checkBox.Checked += OnServerItemCheckedChanged;
                checkBox.Unchecked += OnServerItemCheckedChanged;

                var listItem = new ListBoxItem
                {
                    Content = checkBox,
                    Padding = new Thickness(0)
                };
                _serverListBox.Items.Add(listItem);
            }
        }

        private static ToolTip CreateServerToolTip(McpServerListItem item)
        {
            if (item?.Server == null)
            {
                return null;
            }

            var description = item.Server.Asset?.Description;
            if (string.IsNullOrWhiteSpace(description))
            {
                var transport = string.Equals(item.Server.TransportType, "HTTP", StringComparison.OrdinalIgnoreCase)
                    ? "HTTP"
                    : "stdio";
                description = string.IsNullOrWhiteSpace(item.Server.PluginName)
                    ? $"Transport: {transport}"
                    : $"{item.Server.PluginName} - Transport: {transport}";
            }

            var stack = new StackPanel
            {
                MaxWidth = 250
            };

            stack.Children.Add(new TextBlock
            {
                Text = item.Server.ServerName,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = description,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            return new ToolTip
            {
                Content = stack,
                MaxWidth = 250
            };
        }

        private void OnServerItemCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingServerChecks || sender is not CheckBox checkBox || checkBox.Tag is not McpServerListItem item)
            {
                return;
            }

            _isUpdatingServerChecks = true;

            try
            {
                var isChecked = checkBox.IsChecked == true;
                if (item.IsGroupHeader)
                {
                    foreach (var child in _serverListItems.Where(candidate => !candidate.IsGroupHeader && string.Equals(candidate.GroupKey, item.GroupKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        child.IsChecked = isChecked;
                    }

                    item.IsChecked = isChecked;
                }
                else
                {
                    item.IsChecked = isChecked;
                    UpdateServerGroupHeaderState(item.GroupKey);
                }

                CollectSelectedServers();
            }
            finally
            {
                _isUpdatingServerChecks = false;
            }

            RenderServerListItems();
        }

        private void UpdateServerGroupHeaderState(string groupKey)
        {
            var header = _serverListItems.FirstOrDefault(item => item.IsGroupHeader && string.Equals(item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase));
            if (header == null)
            {
                return;
            }

            var children = _serverListItems
                .Where(item => !item.IsGroupHeader && string.Equals(item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            header.IsChecked = children.Count > 0 && children.All(item => item.IsChecked);
        }

        private void CollectSelectedServers()
        {
            SelectedServers = _serverListItems
                .Where(item => !item.IsGroupHeader && item.IsChecked && item.Server != null)
                .Select(item => new McpServerSelection
                {
                    Asset = item.Server.Asset,
                    ServerName = item.Server.ServerName
                })
                .ToList();
        }

        private void OnInstallClicked()
        {
            CollectSelectedServers();
            if (SelectedServers.Count == 0)
            {
                _ = VS.MessageBox.ShowWarningAsync("No selection", "Select at least one MCP server to install.");
                return;
            }

            DialogResult = true;
            Close();
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

            public string DisplayName { get; set; }

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
            }
        }

        private static Dictionary<string, string> BuildMarketplaceDisplayNames(IReadOnlyList<Services.Marketplace.MarketplaceInfo> allMarketplaces)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (allMarketplaces == null)
            {
                return map;
            }

            foreach (var marketplace in allMarketplaces)
            {
                if (marketplace == null || string.IsNullOrWhiteSpace(marketplace.Id))
                {
                    continue;
                }

                map[marketplace.Id] = string.IsNullOrWhiteSpace(marketplace.DisplayName) ? marketplace.Id : marketplace.DisplayName;
            }

            return map;
        }

        private string ResolveMarketplaceDisplayName(string marketplaceId)
        {
            if (string.IsNullOrWhiteSpace(marketplaceId))
            {
                return marketplaceId;
            }

            if (_marketplaceDisplayNames != null && _marketplaceDisplayNames.TryGetValue(marketplaceId, out string displayName) && !string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            return marketplaceId;
        }

        internal sealed class McpServerSelection
        {
            public PluginAsset Asset { get; init; }

            public string ServerName { get; init; }
        }

        private sealed class McpServerListItem
        {
            public bool IsGroupHeader { get; set; }

            public string GroupKey { get; set; }

            public string GroupDisplayName { get; set; }

            public McpServerItem Server { get; set; }

            public bool IsChecked { get; set; }

            public string DisplayName
            {
                get
                {
                    if (Server == null)
                    {
                        return string.Empty;
                    }

                    string transport = string.Equals(Server.TransportType, "HTTP", StringComparison.OrdinalIgnoreCase)
                        ? "HTTP"
                        : "stdio";
                    return string.IsNullOrEmpty(Server.PluginName)
                        ? $"{Server.ServerName} ({transport})"
                        : $"{Server.ServerName} ({transport}) - {Server.PluginName}";
                }
            }
        }
    }
}
