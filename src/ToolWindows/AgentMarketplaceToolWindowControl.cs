using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitHubNode.Services.Marketplace;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace GitHubNode.ToolWindows
{
    /// <summary>
    /// Control for the Agent Marketplace tool window.
    /// </summary>
    internal sealed partial class AgentMarketplaceToolWindowControl : UserControl
    {
        private ListBox _marketplaceList;
        private TextBox _urlInputTextBox;
        private TextBlock _placeholderText;
        private Button _addButton;
        private Border _refreshAllIconContainer;
        private CrispImage _refreshAllIcon;
        private StackPanel _detailsPanel;
        private Image _detailsAvatar;
        private TextBlock _detailsName;
        private TextBlock _detailsAuthor;
        private TextBlock _detailsUrl;
        private TextBlock _detailsStatus;
        private TreeView _templatesTree;
        private Button _refreshButton;
        private Button _openInBrowserButton;
        private Button _removeButton;
        private TextBlock _statusText;
        private ProgressBar _progressBar;
        private TextBlock _noSelectionText;

        private ObservableCollection<MarketplaceListItem> _marketplaces;
        private CancellationTokenSource _loadCancellationTokenSource;

        public AgentMarketplaceToolWindowControl()
        {
            _marketplaces = new ObservableCollection<MarketplaceListItem>();
            InitializeComponent();
            Loaded += OnControlLoaded;
        }

        private void InitializeComponent()
        {
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            // Main grid with two columns: left list, right details
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300, GridUnitType.Pixel), MinWidth = 200 });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // Splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });

            // Left panel
            var leftPanel = CreateLeftPanel();
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            // Grid splitter
            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            splitter.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            Grid.SetColumn(splitter, 1);
            mainGrid.Children.Add(splitter);

            // Right panel (details)
            var rightPanel = CreateRightPanel();
            Grid.SetColumn(rightPanel, 2);
            mainGrid.Children.Add(rightPanel);

            Content = mainGrid;
        }

        private Grid CreateLeftPanel()
        {
            var grid = new Grid { Margin = new Thickness(12, 12, 6, 12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Add input
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Progress
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status

            // Add input row
            var addInputPanel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            addInputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addInputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Input field with placeholder
            var inputContainer = new Grid();

            _urlInputTextBox = new TextBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent
            };
            _urlInputTextBox.SetResourceReference(ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _urlInputTextBox.SetResourceReference(BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            AutomationProperties.SetName(_urlInputTextBox, "Marketplace or Agent Skills Discovery source");
            AutomationProperties.SetHelpText(_urlInputTextBox, "Enter owner/repo, a repository URL, a domain, or an Agent Skills Discovery index URL");
            _urlInputTextBox.KeyDown += OnUrlInputKeyDown;
            _urlInputTextBox.TextChanged += OnUrlInputTextChanged;
            _urlInputTextBox.GotFocus += OnUrlInputGotFocus;
            _urlInputTextBox.LostFocus += OnUrlInputLostFocus;

            _placeholderText = new TextBlock
            {
                Text = "Enter owner/repo, repository URL, or skill discovery URL...",
                Padding = new Thickness(8, 5, 6, 4),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            _placeholderText.SetResourceReference(ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);

            // Background border for the input
            var inputBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0)
            };
            inputBorder.SetResourceReference(BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            inputBorder.SetResourceReference(BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);

            inputContainer.Children.Add(inputBorder);
            inputContainer.Children.Add(_placeholderText);
            inputContainer.Children.Add(_urlInputTextBox);
            Grid.SetColumn(inputContainer, 0);
            addInputPanel.Children.Add(inputContainer);

            _addButton = CreateThemedButton("Add");
            _addButton.Margin = new Thickness(8, 0, 0, 0);
            _addButton.Click += OnAddButtonClick;
            AutomationProperties.SetName(_addButton, "Add marketplace");
            Grid.SetColumn(_addButton, 1);
            addInputPanel.Children.Add(_addButton);

            Grid.SetRow(addInputPanel, 0);
            grid.Children.Add(addInputPanel);

            // Marketplace list
            _marketplaceList = new ListBox
            {
                ItemsSource = _marketplaces,
                BorderThickness = new Thickness(1)
            };
            _marketplaceList.SetResourceReference(BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _marketplaceList.SetResourceReference(ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _marketplaceList.SetResourceReference(BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            _marketplaceList.ItemTemplate = CreateMarketplaceItemTemplate();
            _marketplaceList.SelectionChanged += OnMarketplaceSelectionChanged;
            AutomationProperties.SetName(_marketplaceList, "Marketplace list");
            Grid.SetRow(_marketplaceList, 1);
            grid.Children.Add(_marketplaceList);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Height = 4,
                IsIndeterminate = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_progressBar, 2);
            grid.Children.Add(_progressBar);

            // Bottom status row with refresh icon + status text
            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            _refreshAllIcon = new CrispImage
            {
                Moniker = KnownMonikers.Refresh,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            _refreshAllIconContainer = new Border
            {
                Child = _refreshAllIcon,
                Padding = new Thickness(0, 0, 8, 0),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _refreshAllIconContainer.MouseLeftButtonUp += OnRefreshAllButtonClick;
            AutomationProperties.SetName(_refreshAllIconContainer, "Refresh all marketplaces");
            AutomationProperties.SetHelpText(_refreshAllIconContainer, "Refresh all marketplaces");
            statusPanel.Children.Add(_refreshAllIconContainer);

            _statusText = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusText.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            statusPanel.Children.Add(_statusText);

            Grid.SetRow(statusPanel, 3);
            grid.Children.Add(statusPanel);

            return grid;
        }

        private DataTemplate CreateMarketplaceItemTemplate()
        {
            var template = new DataTemplate(typeof(MarketplaceListItem));

            // Horizontal layout with avatar and text
            var rowFactory = new FrameworkElementFactory(typeof(StackPanel));
            rowFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            rowFactory.SetValue(StackPanel.MarginProperty, new Thickness(4, 6, 4, 6));

            // Avatar image
            var avatarFactory = new FrameworkElementFactory(typeof(Image));
            avatarFactory.SetValue(Image.WidthProperty, 32.0);
            avatarFactory.SetValue(Image.HeightProperty, 32.0);
            avatarFactory.SetValue(Image.MarginProperty, new Thickness(0, 0, 10, 0));
            avatarFactory.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            avatarFactory.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
            avatarFactory.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("AvatarImage"));
            rowFactory.AppendChild(avatarFactory);

            // Text container
            var textFactory = new FrameworkElementFactory(typeof(StackPanel));
            textFactory.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

            // Name
            var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            nameFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName"));
            nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameFactory.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            textFactory.AppendChild(nameFactory);

            // URL / ID
            var urlFactory = new FrameworkElementFactory(typeof(TextBlock));
            urlFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Id"));
            urlFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            urlFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
            urlFactory.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            urlFactory.SetValue(TextBlock.OpacityProperty, 0.7);
            textFactory.AppendChild(urlFactory);

            // Status badge
            var statusFactory = new FrameworkElementFactory(typeof(TextBlock));
            statusFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusBadge"));
            statusFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
            statusFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
            statusFactory.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            textFactory.AppendChild(statusFactory);

            rowFactory.AppendChild(textFactory);
            template.VisualTree = rowFactory;
            return template;
        }

        private Grid CreateRightPanel()
        {
            var grid = new Grid { Margin = new Thickness(6, 12, 12, 12) };

            // No selection placeholder
            _noSelectionText = new TextBlock
            {
                Text = "Select a marketplace to view details",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };
            _noSelectionText.SetResourceReference(ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            grid.Children.Add(_noSelectionText);

            // Details panel (hidden by default)
            _detailsPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed
            };

            // Header section with avatar
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };

            _detailsAvatar = new Image
            {
                Width = 64,
                Height = 64,
                Margin = new Thickness(0, 0, 16, 0),
                Stretch = Stretch.UniformToFill
            };
            RenderOptions.SetBitmapScalingMode(_detailsAvatar, BitmapScalingMode.HighQuality);
            headerPanel.Children.Add(_detailsAvatar);

            var headerTextPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            _detailsName = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _detailsName.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            headerTextPanel.Children.Add(_detailsName);

            _detailsAuthor = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _detailsAuthor.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            headerTextPanel.Children.Add(_detailsAuthor);

            _detailsUrl = new TextBlock
            {
                FontSize = 12
            };
            _detailsUrl.SetResourceReference(ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
            _detailsUrl.Cursor = System.Windows.Input.Cursors.Hand;
            _detailsUrl.MouseLeftButtonUp += OnUrlClick;
            headerTextPanel.Children.Add(_detailsUrl);

            headerPanel.Children.Add(headerTextPanel);
            _detailsPanel.Children.Add(headerPanel);

            // Status line
            _detailsStatus = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 16)
            };
            _detailsStatus.SetResourceReference(ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            _detailsPanel.Children.Add(_detailsStatus);

            // Separator
            var separator = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 0, 0, 16)
            };
            separator.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            _detailsPanel.Children.Add(separator);

            // Templates section
            var templatesHeader = new TextBlock
            {
                Text = "Templates:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            templatesHeader.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _detailsPanel.Children.Add(templatesHeader);

            _templatesTree = new TreeView
            {
                BorderThickness = new Thickness(0),
                MaxHeight = 300,
                Margin = new Thickness(0, 0, 0, 16)
            };
            _templatesTree.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _templatesTree.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _templatesTree.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            _templatesTree.SetValue(ScrollViewer.CanContentScrollProperty, true);
            _templatesTree.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
            _templatesTree.SetValue(VirtualizingStackPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            _templatesTree.ItemContainerStyle = CreateMarketplaceTreeItemStyle();
            AutomationProperties.SetName(_templatesTree, "Marketplace templates tree");

            _detailsPanel.Children.Add(_templatesTree);

            // Action buttons
            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };

            _refreshButton = CreateThemedButton("Refresh");
            _refreshButton.Click += OnRefreshButtonClick;
            _refreshButton.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(_refreshButton, "Refresh marketplace");
            actionsPanel.Children.Add(_refreshButton);

            _openInBrowserButton = CreateThemedButton("Open in Browser");
            _openInBrowserButton.Click += OnOpenInBrowserClick;
            _openInBrowserButton.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(_openInBrowserButton, "Open in browser");
            actionsPanel.Children.Add(_openInBrowserButton);

            _removeButton = CreateThemedButton("Remove");
            _removeButton.Click += OnRemoveButtonClick;
            AutomationProperties.SetName(_removeButton, "Remove marketplace");
            actionsPanel.Children.Add(_removeButton);

            _detailsPanel.Children.Add(actionsPanel);

            grid.Children.Add(_detailsPanel);

            return grid;
        }

        private static Button CreateThemedButton(string content)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 75,
                Height = 23,
                Padding = new Thickness(8, 0, 8, 0)
            };
            button.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            return button;
        }

        private static Style CreateMarketplaceTreeItemStyle()
        {
            var baseStyle = Application.Current?.TryFindResource(typeof(TreeViewItem)) as Style;
            var style = new Style(typeof(TreeViewItem), baseStyle);
            var selectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(96, 192, 192, 192));
            selectedBackgroundBrush.Freeze();

            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));

            var activeSelectionTrigger = new MultiTrigger();
            activeSelectionTrigger.Conditions.Add(new Condition(TreeViewItem.IsSelectedProperty, true));
            activeSelectionTrigger.Conditions.Add(new Condition(System.Windows.Controls.Primitives.Selector.IsSelectionActiveProperty, true));
            activeSelectionTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selectedBackgroundBrush));
            activeSelectionTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));
            style.Triggers.Add(activeSelectionTrigger);

            var inactiveSelectionTrigger = new MultiTrigger();
            inactiveSelectionTrigger.Conditions.Add(new Condition(TreeViewItem.IsSelectedProperty, true));
            inactiveSelectionTrigger.Conditions.Add(new Condition(System.Windows.Controls.Primitives.Selector.IsSelectionActiveProperty, false));
            inactiveSelectionTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selectedBackgroundBrush));
            inactiveSelectionTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));
            style.Triggers.Add(inactiveSelectionTrigger);

            return style;
        }

        private async void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            await LoadMarketplacesAsync();
        }

        private void OnUrlInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                OnAddButtonClick(sender, null);
            }
        }

        private void OnUrlInputTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        private void OnUrlInputGotFocus(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        private void OnUrlInputLostFocus(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        private void UpdatePlaceholderVisibility()
        {
            _placeholderText.Visibility = string.IsNullOrEmpty(_urlInputTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void OnAddButtonClick(object sender, RoutedEventArgs e)
        {
            var input = _urlInputTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                await VS.MessageBox.ShowWarningAsync("Invalid Input", "Please enter owner/repo, a repository URL, a domain, or an Agent Skills Discovery index URL.");
                _urlInputTextBox.Focus();
                return;
            }

            if (AgentSkillsDiscoveryService.TryCreateIndexUri(input, out var indexUri))
            {
                var confirmed = await VS.MessageBox.ShowConfirmAsync(
                    "Trust Agent Skills Source",
                    $"Add and trust skills from {indexUri.GetLeftPart(UriPartial.Authority)}?\n\nSkills contain instructions that can be loaded into agent context. Only add sources you trust.");

                if (!confirmed)
                {
                    return;
                }
            }

            SetLoading(true, $"Adding {input}...");

            try
            {
                var (success, error, marketplace) = await MarketplaceService.AddMarketplaceAsync(
                    input,
                    _loadCancellationTokenSource?.Token ?? CancellationToken.None);

                if (success && marketplace != null)
                {
                    _urlInputTextBox.Clear();
                    await LoadMarketplacesAsync();
                    SetStatus($"Added {marketplace.DisplayName}");
                }
                else
                {
                    SetStatus(error ?? "Failed to add marketplace");
                    await VS.MessageBox.ShowWarningAsync("Failed to Add Marketplace", error ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async void OnRemoveButtonClick(object sender, RoutedEventArgs e)
        {
            var selected = _marketplaceList.SelectedItem as MarketplaceListItem;
            if (selected == null || selected.IsBuiltIn)
            {
                return;
            }

            var confirm = await VS.MessageBox.ShowConfirmAsync(
                "Remove Marketplace",
                $"Remove '{selected.DisplayName}' from your registered marketplaces?\n\nThis will also delete the local cache.");

            if (!confirm)
            {
                return;
            }

            try
            {
                MarketplaceService.RemoveMarketplace(selected.Owner, selected.RepoName, selected.RepositoryUrl, deleteClone: true);
                await LoadMarketplacesAsync();
                SetStatus($"Removed {selected.DisplayName}");
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                SetStatus($"Error: {ex.Message}");
            }
        }

        private async void OnRefreshButtonClick(object sender, RoutedEventArgs e)
        {
            var selected = _marketplaceList.SelectedItem as MarketplaceListItem;
            if (selected == null)
            {
                return;
            }

            SetLoading(true, $"Refreshing {selected.DisplayName}...");
            try
            {
                await MarketplaceService.GetMarketplaceAsync(
                    selected.Owner,
                    selected.RepoName,
                    repositoryUrl: selected.RepositoryUrl,
                    forceRefresh: true,
                    cancellationToken: _loadCancellationTokenSource?.Token ?? CancellationToken.None);
                await LoadMarketplacesAsync(selectionToRestore: selected);
                SetStatus($"Refreshed {selected.DisplayName}");
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async void OnRefreshAllButtonClick(object sender, RoutedEventArgs e)
        {
            var confirmed = await VS.MessageBox.ShowConfirmAsync(
                "Refresh Marketplaces",
                "This will refresh all marketplace repositories and Agent Skills Discovery sources. Continue?");

            if (!confirmed)
            {
                return;
            }

            await LoadMarketplacesAsync(forceRefresh: true);
        }

        private void OnOpenInBrowserClick(object sender, RoutedEventArgs e)
        {
            var selected = _marketplaceList.SelectedItem as MarketplaceListItem;
            if (selected != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = selected.GitHubUrl,
                    UseShellExecute = true
                });
            }
        }

        private void OnUrlClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = _marketplaceList.SelectedItem as MarketplaceListItem;
            if (selected != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = selected.GitHubUrl,
                    UseShellExecute = true
                });
            }
        }

        private void OnMarketplaceSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selected = _marketplaceList.SelectedItem as MarketplaceListItem;
            if (selected == null)
            {
                _noSelectionText.Visibility = Visibility.Visible;
                _detailsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _noSelectionText.Visibility = Visibility.Collapsed;
            _detailsPanel.Visibility = Visibility.Visible;

            // Update details
            _detailsAvatar.Source = selected.AvatarImage;
            _detailsName.Text = selected.DisplayName;
            _detailsAuthor.Text = selected.IsAgentSkillsDiscovery ? "Agent Skills Discovery" : $"by {selected.Owner}";
            _detailsUrl.Text = selected.GitHubUrl;
            _detailsStatus.Text = selected.StatusLine;

            // Update categorized templates
            PopulateTemplateCategories(selected.TemplatesByCategory);

            // Update button state
            _removeButton.IsEnabled = !selected.IsBuiltIn;
        }

        private void PopulateTemplateCategories(System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<TemplateListItem>> templatesByCategory)
        {
            _templatesTree.Items.Clear();

            // Define the display order and friendly names for categories
            var categoryOrder = new[] { "Agents", "Skills", "Instructions", "MCP Servers", "Prompts" };

            foreach (var categoryName in categoryOrder)
            {
                if (!templatesByCategory.TryGetValue(categoryName, out var templates) || templates.Count == 0)
                {
                    continue;
                }

                var categoryNode = new TreeViewItem
                {
                    IsExpanded = true,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                // Header with count
                var header = new TextBlock
                {
                    Text = $"{categoryName} ({templates.Count})",
                    FontWeight = FontWeights.SemiBold
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
                categoryNode.Header = header;

                foreach (var template in templates)
                {
                    var itemNode = new TreeViewItem
                    {
                        Margin = new Thickness(0, 2, 0, 2)
                    };

                    var nameText = new TextBlock
                    {
                        Text = template.DisplayName
                    };
                    nameText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
                    itemNode.Header = nameText;
                    itemNode.ToolTip = CreateTemplateTooltip(template);
                    categoryNode.Items.Add(itemNode);
                }

                _templatesTree.Items.Add(categoryNode);
            }

            // If no templates at all, show a message
            if (_templatesTree.Items.Count == 0)
            {
                var noTemplatesNode = new TreeViewItem
                {
                    Header = new TextBlock
                    {
                        Text = "No templates available",
                        FontStyle = FontStyles.Italic
                    },
                    IsEnabled = false
                };
                if (noTemplatesNode.Header is TextBlock noTemplatesText)
                {
                    noTemplatesText.SetResourceReference(ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                }

                _templatesTree.Items.Add(noTemplatesNode);
            }
        }

        private static ToolTip CreateTemplateTooltip(TemplateListItem template)
        {
            if (template == null)
            {
                return null;
            }

            var description = string.IsNullOrWhiteSpace(template.Description)
                ? "No description available."
                : template.Description;

            var stack = new StackPanel
            {
                MaxWidth = 250
            };

            stack.Children.Add(new TextBlock
            {
                Text = template.DisplayName,
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

        private async Task LoadMarketplacesAsync(bool forceRefresh = false, MarketplaceListItem selectionToRestore = null)
        {
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _loadCancellationTokenSource.Token;
            var selectedMarketplace = selectionToRestore ?? (_marketplaceList?.SelectedItem as MarketplaceListItem);

            try
            {
                SetLoading(true, "Loading marketplaces...");
                _marketplaces.Clear();

                var marketplaces = await MarketplaceService.GetAllMarketplacesAsync(forceRefresh, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var marketplace in marketplaces)
                {
                    _marketplaces.Add(new MarketplaceListItem(marketplace));
                }

                RestoreMarketplaceSelection(selectedMarketplace);

                var syncedCount = 0;
                var totalCount = marketplaces.Count;
                foreach (var m in marketplaces)
                {
                    if (m.IsCloned)
                    {
                        syncedCount++;
                    }
                }

                SetStatus($"{totalCount} marketplaces registered, {syncedCount} synced");
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                SetStatus($"Error loading marketplaces: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void RestoreMarketplaceSelection(MarketplaceListItem selectedMarketplace)
        {
            if (_marketplaceList == null)
            {
                return;
            }

            if (selectedMarketplace == null)
            {
                if (_marketplaces.Count > 0)
                {
                    _marketplaceList.SelectedIndex = 0;
                }

                return;
            }

            foreach (var item in _marketplaces)
            {
                if (IsMatchingMarketplace(item, selectedMarketplace))
                {
                    _marketplaceList.SelectedItem = item;
                    _marketplaceList.ScrollIntoView(item);
                    return;
                }
            }

            if (_marketplaces.Count > 0)
            {
                _marketplaceList.SelectedIndex = 0;
            }
        }

        private static bool IsMatchingMarketplace(MarketplaceListItem candidate, MarketplaceListItem selectedMarketplace)
        {
            return string.Equals(candidate.Owner, selectedMarketplace.Owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.RepoName, selectedMarketplace.RepoName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.RepositoryUrl, selectedMarketplace.RepositoryUrl, StringComparison.OrdinalIgnoreCase);
        }

        private void SetLoading(bool isLoading, string message = null)
        {
            _progressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            _addButton.IsEnabled = !isLoading;
            _refreshAllIconContainer.IsHitTestVisible = !isLoading;
            _refreshAllIcon.Opacity = isLoading ? 0.45 : 1;

            if (!string.IsNullOrEmpty(message))
            {
                SetStatus(message);
            }
        }

        private void SetStatus(string message)
        {
            _statusText.Text = message;
        }

        /// <summary>
        /// Item representing a marketplace in the list.
        /// </summary>
        private sealed class MarketplaceListItem
        {
            public string Id { get; }
            public string DisplayName { get; }
            public string Owner { get; }
            public string RepoName { get; }
            public string RepositoryUrl { get; }
            public bool IsBuiltIn { get; }
            public bool IsCloned { get; }
            public bool IsAgentSkillsDiscovery { get; }
            public string ErrorMessage { get; }
            public string GitHubUrl { get; }
            public DateTime? LastUpdated { get; }
            public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<TemplateListItem>> TemplatesByCategory { get; }
            public int TotalTemplateCount { get; }
            public BitmapImage AvatarImage { get; }

            public string StatusBadge
            {
                get
                {
                    if (!string.IsNullOrEmpty(ErrorMessage))
                    {
                        return "Error";
                    }

                    if (IsBuiltIn)
                    {
                        return "Built-in";
                    }

                    if (IsAgentSkillsDiscovery)
                    {
                        return IsCloned ? "Synced" : "Not synced";
                    }

                    return IsCloned ? "Cloned" : "Not cloned";
                }
            }

            public string StatusLine
            {
                get
                {
                    var parts = new System.Collections.Generic.List<string>();

                    if (TotalTemplateCount > 0)
                    {
                        parts.Add($"{TotalTemplateCount} templates");
                    }

                    if (LastUpdated.HasValue)
                    {
                        var timeAgo = GetTimeAgo(LastUpdated.Value);
                        parts.Add($"Last synced: {timeAgo}");
                    }
                    else if (!IsCloned)
                    {
                        parts.Add(IsAgentSkillsDiscovery ? "Not yet synced" : "Not yet cloned");
                    }

                    if (!string.IsNullOrEmpty(ErrorMessage))
                    {
                        parts.Add($"Error: {ErrorMessage}");
                    }

                    return string.Join(" - ", parts);
                }
            }

            public MarketplaceListItem(MarketplaceInfo marketplace)
            {
                Id = marketplace.Id;
                DisplayName = marketplace.DisplayName ?? marketplace.Id;
                Owner = marketplace.Owner;
                RepoName = marketplace.RepoName;
                RepositoryUrl = marketplace.RepositoryUrl;
                IsBuiltIn = marketplace.IsBuiltIn;
                IsCloned = marketplace.IsCloned;
                IsAgentSkillsDiscovery = marketplace.IsAgentSkillsDiscovery;
                ErrorMessage = marketplace.ErrorMessage;
                GitHubUrl = marketplace.GitHubUrl ?? marketplace.CloneUrl;
                LastUpdated = marketplace.LastUpdated;

                AvatarImage = LoadMarketplaceImage(marketplace.IconPath, marketplace.Owner, marketplace.GitHubUrl);

                // Organize templates by category
                TemplatesByCategory = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<TemplateListItem>>();
                var totalCount = 0;

                foreach (var plugin in marketplace.Plugins)
                {
                    foreach (var asset in plugin.Assets)
                    {
                        var categoryName = GetCategoryName(asset.Type);

                        if (!TemplatesByCategory.TryGetValue(categoryName, out var list))
                        {
                            list = new System.Collections.Generic.List<TemplateListItem>();
                            TemplatesByCategory[categoryName] = list;
                        }

                        // For MCP servers, extract the actual server names from the config file
                        if (asset.Type == AssetType.McpServer)
                        {
                            var serverNames = GetMcpServerNames(asset);
                            foreach (var serverName in serverNames)
                            {
                                list.Add(new TemplateListItem(serverName, asset.Description));
                                totalCount++;
                            }
                        }
                        else
                        {
                            var displayName = asset.Name ?? System.IO.Path.GetFileNameWithoutExtension(asset.RelativePath);
                            list.Add(new TemplateListItem(displayName, asset.Description));
                            totalCount++;
                        }
                    }
                }

                TotalTemplateCount = totalCount;
            }

            private static string GetCategoryName(AssetType type)
            {
                switch (type)
                {
                    case AssetType.Agent:
                        return "Agents";
                    case AssetType.Skill:
                        return "Skills";
                    case AssetType.Instructions:
                        return "Instructions";
                    case AssetType.McpServer:
                        return "MCP Servers";
                    case AssetType.Prompt:
                        return "Prompts";
                    default:
                        return "Other";
                }
            }

            /// <summary>
            /// Extracts MCP server names from the .mcp.json config file.
            /// Returns the actual server names defined in the JSON, or a fallback name if parsing fails.
            /// </summary>
            private static System.Collections.Generic.List<string> GetMcpServerNames(PluginAsset asset)
            {
                var names = new System.Collections.Generic.List<string>();

                try
                {
                    if (!string.IsNullOrEmpty(asset.LocalPath) && System.IO.File.Exists(asset.LocalPath))
                    {
                        var json = System.IO.File.ReadAllText(asset.LocalPath);
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
                            // No servers found, use fallback
                            names.Add(GetMcpFallbackName(asset));
                            return names;
                        }

                        // Add each server name defined in the config file
                        foreach (var serverProp in serversElement.EnumerateObject())
                        {
                            names.Add(serverProp.Name);
                        }
                    }
                }
                catch
                {
                    // Parsing failed, ignore
                }

                // If no names were extracted, use fallback
                if (names.Count == 0)
                {
                    names.Add(GetMcpFallbackName(asset));
                }

                return names;
            }

            /// <summary>
            /// Gets a fallback display name for an MCP server asset when parsing fails.
            /// Uses the parent folder name or plugin name instead of the filename.
            /// </summary>
            private static string GetMcpFallbackName(PluginAsset asset)
            {
                if (!string.IsNullOrEmpty(asset.LocalPath))
                {
                    var parentFolder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(asset.LocalPath));
                    if (!string.IsNullOrEmpty(parentFolder) && parentFolder != "plugins")
                    {
                        return parentFolder;
                    }
                }

                return asset.PluginName ?? "MCP Server";
            }

            private static BitmapImage LoadMarketplaceImage(string iconPath, string owner, string repositoryUrl)
            {
                var iconImage = LoadLocalImage(iconPath);
                return iconImage ?? LoadAvatarImage(owner, repositoryUrl);
            }

            private static BitmapImage LoadLocalImage(string iconPath)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(iconPath) || !System.IO.File.Exists(iconPath))
                    {
                        return null;
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 64;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch (Exception ex)
                {
                    _ = ex.LogAsync();
                    return null;
                }
            }

            private static BitmapImage LoadAvatarImage(string owner, string repositoryUrl)
            {
                try
                {
                    if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri) ||
                        (!string.Equals(repositoryUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                         !repositoryUri.Host.EndsWith(".ghe.com", StringComparison.OrdinalIgnoreCase)))
                    {
                        return null;
                    }

                    var avatarUrl = $"https://{repositoryUri.Authority}/{owner}.png?size=64";
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(avatarUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 64;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch (Exception ex)
                {
                    _ = ex.LogAsync();
                    return null;
                }
            }

            private static string GetTimeAgo(DateTime dateTime)
            {
                var span = DateTime.UtcNow - dateTime.ToUniversalTime();

                if (span.TotalMinutes < 1)
                {
                    return "just now";
                }

                if (span.TotalHours < 1)
                {
                    var minutes = (int)span.TotalMinutes;
                    return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
                }

                if (span.TotalDays < 1)
                {
                    var hours = (int)span.TotalHours;
                    return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
                }

                if (span.TotalDays < 30)
                {
                    var days = (int)span.TotalDays;
                    return days == 1 ? "1 day ago" : $"{days} days ago";
                }

                return dateTime.ToString("MMM d, yyyy");
            }
        }

        /// <summary>
        /// Item representing a template in the details panel.
        /// </summary>
        private sealed class TemplateListItem
        {
            public string DisplayName { get; }
            public string Description { get; }

            public TemplateListItem(string displayName, string description)
            {
                DisplayName = displayName;
                Description = description;
            }
        }
    }
}
