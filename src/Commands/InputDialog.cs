using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Automation;
using GitHubNode.Services;
using GitHubNode.Services.Marketplace;
using Microsoft.VisualStudio.PlatformUI;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A simple input dialog for prompting the user for text input.
    /// Uses Visual Studio theming for consistent appearance.
    /// Supports optional template dropdown loaded from marketplace repositories.
    /// </summary>
    internal sealed class InputDialog : DialogWindow
    {
        private const int _dwmwaUseImmersiveDarkMode = 20;
        private const int _dwmwaCaptionColor = 35;
        private const int _dwmwaTextColor = 36;
        private const string _allMarketplacesText = "All Marketplaces";
        private const string _settingsKey = "InputDialog";

        private readonly TextBox _textBox;
        private readonly RichTextBox _previewBox;
        private readonly CheckBox _userProfileCheckBox;
        private readonly ComboBox _providerComboBox;
        private readonly TextBox _searchBox;
        private readonly TreeView _templateTreeView;
        private readonly TextBlock _templateLabel;
        private readonly TextBlock _statusText;
        private readonly Button _refreshButton;
        private readonly Func<string, string> _previewGenerator;
        private readonly TemplateType? _templateType;
        private readonly string _defaultFileName;

        private List<MarketplaceAsProvider> _marketplaceProviders;
        private List<TemplateInfo> _allTemplates;
        private List<TemplateInfo> _filteredTemplates;
        private List<TemplateListItemModel> _templateListItems;
        private readonly Dictionary<string, bool> _collapsedTemplateGroups;
        private readonly Dictionary<TemplateInfo, ToolTip> _templateTooltipCache;
        private readonly HashSet<string> _selectedTemplateKeys;
        private readonly HashSet<string> _preselectedTemplateFileNames;
        private bool _isUpdatingTemplateChecks;
        private CancellationTokenSource _templateListCancellationTokenSource;
        private CancellationTokenSource _templateContentCancellationTokenSource;
        private bool _isInitialLoad;
        private bool _hasAppliedPreselectedTemplates;

        /// <summary>
        /// Gets the text entered by the user.
        /// </summary>
        public string InputText => _textBox.Text;

        /// <summary>
        /// Gets the selected installation scope.
        /// </summary>
        public Services.InstallScope SelectedScope =>
            _userProfileCheckBox?.IsChecked == true ? Services.InstallScope.UserProfile : Services.InstallScope.Solution;

        /// <summary>
        /// Gets the content to use for the file.
        /// Returns the selected template content, or null if using custom/default template.
        /// </summary>
        public string SelectedTemplateContent { get; private set; }

        /// <summary>
        /// Gets the selected templates to create.
        /// </summary>
        public IReadOnlyList<TemplateSelectionResult> SelectedTemplates { get; private set; } = [];

        /// <summary>
        /// Creates a new input dialog.
        /// </summary>
        public InputDialog(
            string title,
            string prompt,
            string defaultValue = "",
            Func<string, string> previewGenerator = null,
            TemplateType? templateType = null,
            IReadOnlyList<MarketplaceAsProvider> marketplaceProviders = null,
            IReadOnlyCollection<string> preselectedTemplateFileNames = null)
        {
            _previewGenerator = previewGenerator;
            _templateType = templateType;
            _marketplaceProviders = marketplaceProviders == null
                ? new List<MarketplaceAsProvider>()
                : new List<MarketplaceAsProvider>(marketplaceProviders);
            _defaultFileName = defaultValue;

            Title = title;
            Width = 550;
            MinWidth = 400;
            MinHeight = 300;
            Height = previewGenerator != null || templateType != null ? 500 : double.NaN;
            SizeToContent = previewGenerator != null || templateType != null ? SizeToContent.Manual : SizeToContent.Height;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;
            HasMinimizeButton = false;
            HasHelpButton = false;
            ShowInTaskbar = false;

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
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            _allTemplates = new List<TemplateInfo>();
            _filteredTemplates = new List<TemplateInfo>();
            _templateListItems = new List<TemplateListItemModel>();
            _collapsedTemplateGroups = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
            _templateTooltipCache = new Dictionary<TemplateInfo, ToolTip>();
            _selectedTemplateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _preselectedTemplateFileNames = preselectedTemplateFileNames == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(preselectedTemplateFileNames, StringComparer.OrdinalIgnoreCase);

            // Constants for two-column layout
            const double labelWidth = 100;
            const double rowSpacing = 8;

            var grid = new Grid
            {
                Margin = new Thickness(12)
            };

            // Define two columns: fixed-width labels, remaining space for inputs
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Only add prompt row for non-template dialogs
            if (templateType == null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt
            }

            // Always add rows for scope, marketplace, search, and template when templateType is set
            if (templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Scope checkbox
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Marketplace
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Template label
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Template list
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // File name

            if (previewGenerator != null && templateType == null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preview label
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Preview box
            }

            if (templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status text
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            var currentRow = 0;

            // Only show prompt for non-template dialogs (template dialogs use descriptive title instead)
            if (templateType == null)
            {
                var label = new TextBlock
                {
                    Text = prompt,
                    Margin = new Thickness(0, 0, 0, rowSpacing),
                    TextWrapping = TextWrapping.Wrap
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(label, currentRow);
                Grid.SetColumnSpan(label, 2);
                grid.Children.Add(label);
                currentRow++;
            }

            if (templateType != null)
            {
                // Scope checkbox (spans both columns)
                _userProfileCheckBox = new CheckBox
                {
                    Content = "Install to User Profile (all solutions)",
                    Margin = new Thickness(0, 0, 0, rowSpacing),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                _userProfileCheckBox.SetResourceReference(CheckBox.StyleProperty, VsResourceKeys.CheckBoxStyleKey);
                _userProfileCheckBox.SetResourceReference(CheckBox.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                AutomationProperties.SetName(_userProfileCheckBox, "Install to User Profile");
                AutomationProperties.SetHelpText(_userProfileCheckBox, "Check to install to your User Profile for all solutions. Uncheck to install to Solution (shared with team).");
                Grid.SetRow(_userProfileCheckBox, currentRow);
                Grid.SetColumnSpan(_userProfileCheckBox, 2);
                grid.Children.Add(_userProfileCheckBox);
                currentRow++;

                // Marketplace row
                var providerLabel = new TextBlock
                {
                    Text = "Marketplace:",
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                providerLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(providerLabel, currentRow);
                Grid.SetColumn(providerLabel, 0);
                grid.Children.Add(providerLabel);

                _providerComboBox = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, rowSpacing),
                    IsEditable = false
                };
                _providerComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
                _providerComboBox.Items.Add(_allMarketplacesText);
                _providerComboBox.SelectedIndex = 0;
                _providerComboBox.SelectionChanged += OnProviderSelectionChanged;
                AutomationProperties.SetName(_providerComboBox, "Marketplace filter");
                AutomationProperties.SetHelpText(_providerComboBox, "Filter templates by marketplace, or select 'All Marketplaces' to see all.");
                Grid.SetRow(_providerComboBox, currentRow);
                Grid.SetColumn(_providerComboBox, 1);
                grid.Children.Add(_providerComboBox);
                currentRow++;

                // Search row
                var searchLabel = new TextBlock
                {
                    Text = "Search:",
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                searchLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(searchLabel, currentRow);
                Grid.SetColumn(searchLabel, 0);
                grid.Children.Add(searchLabel);

                _searchBox = new TextBox
                {
                    Margin = new Thickness(0, 0, 0, rowSpacing),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                _searchBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
                _searchBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
                _searchBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
                _searchBox.TextChanged += OnSearchTextChanged;
                AutomationProperties.SetName(_searchBox, "Search templates");
                AutomationProperties.SetHelpText(_searchBox, "Type to filter templates by name.");
                Grid.SetRow(_searchBox, currentRow);
                Grid.SetColumn(_searchBox, 1);
                grid.Children.Add(_searchBox);
                currentRow++;

                // Template row
                _templateLabel = new TextBlock
                {
                    Text = "Templates:",
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                _templateLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(_templateLabel, currentRow);
                Grid.SetColumn(_templateLabel, 0);
                grid.Children.Add(_templateLabel);
                currentRow++;

                _templateTreeView = new TreeView
                {
                    Margin = new Thickness(0, 0, 0, rowSpacing),
                    MinHeight = 160,
                    BorderThickness = new Thickness(1)
                };
                _templateTreeView.SetResourceReference(TreeView.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
                _templateTreeView.SetResourceReference(TreeView.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
                _templateTreeView.SetResourceReference(TreeView.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
                AutomationProperties.SetName(_templateTreeView, "Template selection tree");
                AutomationProperties.SetHelpText(_templateTreeView, "Check one or more templates to add. Expand a marketplace group to select individual templates, or use the group checkbox to select or clear all templates in that marketplace.");
                Grid.SetRow(_templateTreeView, currentRow);
                Grid.SetColumnSpan(_templateTreeView, 2);
                grid.Children.Add(_templateTreeView);
                currentRow++;
            }

            // File name row
            var fileNameLabel = new TextBlock
            {
                Text = "File name:",
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            fileNameLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(fileNameLabel, currentRow);
            Grid.SetColumn(fileNameLabel, 0);
            grid.Children.Add(fileNameLabel);

            _textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, rowSpacing),
                Padding = new Thickness(4, 2, 4, 2)
            };
            _textBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _textBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _textBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            _textBox.TextChanged += OnFileNameTextChanged;
            AutomationProperties.SetName(_textBox, "File name");
            AutomationProperties.SetHelpText(_textBox, "Enter the name of the file to create. Leave all templates unchecked to create a scaffolded file from this name.");
            Grid.SetRow(_textBox, currentRow);
            Grid.SetColumn(_textBox, 1);
            grid.Children.Add(_textBox);
            currentRow++;

            if (previewGenerator != null && templateType == null)
            {
                var previewLabel = new TextBlock
                {
                    Text = "Preview:",
                    Margin = new Thickness(0, 0, 0, 4)
                };
                previewLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(previewLabel, currentRow);
                Grid.SetColumnSpan(previewLabel, 2);
                grid.Children.Add(previewLabel);
                currentRow++;

                _previewBox = new RichTextBox
                {
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, rowSpacing),
                    Padding = new Thickness(4),
                    BorderThickness = new Thickness(1)
                };
                _previewBox.SetResourceReference(RichTextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
                _previewBox.SetResourceReference(RichTextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
                _previewBox.SetResourceReference(RichTextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
                AutomationProperties.SetName(_previewBox, "Template preview");
                AutomationProperties.SetHelpText(_previewBox, "Read-only preview of the selected template content.");

                Grid.SetRow(_previewBox, currentRow);
                Grid.SetColumnSpan(_previewBox, 2);
                grid.Children.Add(_previewBox);
                currentRow++;
            }

            if (templateType != null)
            {
                _statusText = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, rowSpacing)
                };
                _statusText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                AutomationProperties.SetName(_statusText, "Template status");

                Grid.SetRow(_statusText, currentRow);
                Grid.SetColumnSpan(_statusText, 2);
                grid.Children.Add(_statusText);
                currentRow++;
            }

            var buttonRowGrid = new Grid();
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(buttonRowGrid, currentRow);
            Grid.SetColumnSpan(buttonRowGrid, 2);

            if (templateType != null)
            {
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
                    ToolTip = "Refresh templates from GitHub (F5)",
                    Margin = new Thickness(0, 0, 4, 0)
                };
                _refreshButton.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
                _refreshButton.Click += OnRefreshButtonClick;
                AutomationProperties.SetName(_refreshButton, "Refresh templates");
                AutomationProperties.SetHelpText(_refreshButton, "Fetch templates again from GitHub.");
                actionPanel.Children.Add(_refreshButton);

                Grid.SetColumn(actionPanel, 0);
                buttonRowGrid.Children.Add(actionPanel);
            }

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button okButton = CreateThemedButton("OK", isDefault: true);
            okButton.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(okButton, "OK");
            okButton.Click += (s, e) =>
            {
                if (!TryFinalizeSelection())
                {
                    return;
                }

                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(okButton);

            Button cancelButton = CreateThemedButton("Cancel", isCancel: true);
            AutomationProperties.SetName(cancelButton, "Cancel");
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

            ContentRendered += OnContentRendered;
            SourceInitialized += OnSourceInitialized;
            Closing += OnDialogClosing;
            KeyDown += OnKeyDown;

            RestoreDialogSize();

            // Disable all input controls initially if we need to load marketplaces
            if (templateType != null && _marketplaceProviders.Count == 0)
            {
                _isInitialLoad = true;
                SetInputControlsEnabled(false);
            }
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

            color = default(Color);
            return false;
        }

        private static int ToColorRef(Color color)
            => color.R | (color.G << 8) | (color.B << 16);

        private void RestoreDialogSize()
        {
            try
            {
                Dictionary<string, double> settings = GitHubNodePackage.Instance?.DialogSettings;
                if (settings != null
                    && settings.TryGetValue(_settingsKey + "_Width", out var width)
                    && settings.TryGetValue(_settingsKey + "_Height", out var height)
                    && width >= MinWidth
                    && height >= MinHeight)
                {
                    Width = width;
                    Height = height;
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private void SaveDialogSize()
        {
            try
            {
                Dictionary<string, double> settings = GitHubNodePackage.Instance?.DialogSettings;
                if (settings != null)
                {
                    settings[_settingsKey + "_Width"] = ActualWidth;
                    settings[_settingsKey + "_Height"] = ActualHeight;
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private void OnDialogClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CancelAndDispose(ref _templateListCancellationTokenSource);
            CancelAndDispose(ref _templateContentCancellationTokenSource);
            SaveDialogSize();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5 && _refreshButton != null && _refreshButton.IsEnabled)
            {
                OnRefreshButtonClick(_refreshButton, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private async void OnContentRendered(object sender, EventArgs e)
        {
            _textBox.Focus();
            _textBox.SelectAll();
            UpdatePreview();

            if (_templateType != null && _templateTreeView != null)
            {
                await ExecuteUiActionAsync(() => LoadTemplatesAsync(), "Failed to load templates");
            }
        }

        private async Task LoadTemplatesAsync(bool forceRefresh = false)
        {
            CancellationTokenSource currentRequestCancellation = ReplaceCancellationTokenSource(ref _templateListCancellationTokenSource);
            CancellationToken cancellationToken = currentRequestCancellation.Token;
            CancelAndDispose(ref _templateContentCancellationTokenSource);

            try
            {
                // Load providers if not yet loaded (this clones marketplace repos on first run)
                if (_marketplaceProviders.Count == 0)
                {
                    SetStatus("Initializing marketplaces (first run may take a moment)...");
                    SetRefreshEnabled(false);

                    _marketplaceProviders = await MarketplaceTemplateAdapter.GetProvidersForTemplateTypeAsync(_templateType.Value, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    // Update provider dropdown with "All Marketplaces" + individual marketplaces
                    if (_providerComboBox != null)
                    {
                        _providerComboBox.Items.Clear();
                        _providerComboBox.Items.Add(_allMarketplacesText);

                        foreach (var p in _marketplaceProviders)
                        {
                            _providerComboBox.Items.Add(p);
                        }

                        _providerComboBox.SelectedIndex = 0; // "All Marketplaces"
                    }
                }
                else
                {
                    SetStatus("Loading templates...");
                    SetRefreshEnabled(false);
                }

                if (_marketplaceProviders.Count == 0)
                {
                    SetStatus("No marketplaces available. Check your internet connection or use Manage Marketplaces.");
                    return;
                }

                // Load all templates from all providers in parallel for better performance
                var templateTasks = _marketplaceProviders.Select(provider =>
                    MarketplaceTemplateAdapter.GetTemplatesAsync(_templateType.Value, provider?.Marketplace, cancellationToken));

                var templateResults = await Task.WhenAll(templateTasks);
                cancellationToken.ThrowIfCancellationRequested();

                _allTemplates = templateResults.SelectMany(t => t).ToList();
                ApplyPreselectedTemplates();

                // Apply filters and update UI
                ApplyFilters();

                var totalCount = _allTemplates.Count;
                var marketplaceCount = _marketplaceProviders.Count;
                SetStatus($"Loaded {totalCount} templates from {marketplaceCount} marketplace{(marketplaceCount != 1 ? "s" : "")}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (ReferenceEquals(_templateListCancellationTokenSource, currentRequestCancellation))
                {
                    _templateListCancellationTokenSource = null;
                    SetRefreshEnabled(true);

                    // Re-enable input controls after initial load
                    if (_isInitialLoad)
                    {
                        _isInitialLoad = false;
                        SetInputControlsEnabled(true);
                    }

                    currentRequestCancellation.Dispose();
                }
            }
        }

        private void ApplyFilters()
        {
            string selectedMarketplaceId = null;
            if (_providerComboBox?.SelectedItem is MarketplaceAsProvider selectedProvider)
            {
                selectedMarketplaceId = selectedProvider.Id;
            }

            string searchText = _searchBox?.Text?.Trim() ?? "";
            _filteredTemplates = _allTemplates
                .Where(t =>
                {
                    if (selectedMarketplaceId != null && !string.Equals(t.ProviderId, selectedMarketplaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (!string.IsNullOrEmpty(searchText))
                    {
                        var name = t.DisplayName ?? t.Name ?? t.FileName ?? "";
                        var category = t.Category ?? "";
                        if (name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0
                            && category.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .ToList();

            bool showGrouped = selectedMarketplaceId == null;
            UpdateTemplateList(showGrouped);
        }

        private void UpdateTemplateList(bool groupByMarketplace)
        {
            if (_templateTreeView == null)
            {
                return;
            }

            foreach (var existingHeader in _templateListItems.Where(item => item.IsGroupHeader && !string.IsNullOrEmpty(item.GroupKey)))
            {
                _collapsedTemplateGroups[existingHeader.GroupKey] = existingHeader.IsCollapsed;
            }

            var nextItems = new List<TemplateListItemModel>();

            if (_filteredTemplates.Count > 0)
            {
                var marketplaceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _marketplaceProviders.Count; i++)
                {
                    var providerId = _marketplaceProviders[i].Id;
                    if (!string.IsNullOrEmpty(providerId) && !marketplaceOrder.ContainsKey(providerId))
                    {
                        marketplaceOrder[providerId] = i;
                    }
                }

                var grouped = _filteredTemplates
                    .GroupBy(t => t.ProviderId ?? "unknown")
                    .OrderBy(g => marketplaceOrder.TryGetValue(g.Key, out int order) ? order : int.MaxValue);

                foreach (var group in grouped)
                {
                    var templatesInGroup = group
                        .OrderBy(t => t.DisplayName ?? t.Name ?? t.FileName)
                        .ToList();

                    var groupItems = templatesInGroup
                        .Select(template => new TemplateListItemModel
                        {
                            IsGroupHeader = false,
                            GroupKey = group.Key,
                            GroupDisplayName = GetMarketplaceDisplayName(group.Key),
                            Template = template,
                            IsChecked = _selectedTemplateKeys.Contains(TemplateSelectionKey.Create(template))
                        })
                        .ToList();

                    nextItems.Add(new TemplateListItemModel
                    {
                        IsGroupHeader = true,
                        GroupKey = group.Key,
                        GroupDisplayName = GetMarketplaceDisplayName(group.Key),
                        IsCollapsed = _collapsedTemplateGroups.TryGetValue(group.Key, out var isCollapsed) && isCollapsed,
                        IsChecked = groupItems.Count > 0 && groupItems.All(item => item.IsChecked)
                    });

                    nextItems.AddRange(groupItems);
                }
            }

            _templateListItems = nextItems;
            RenderTemplateListItems();
            UpdateTemplateLabelSelectionCount();
        }

        private string GetMarketplaceDisplayName(string marketplaceId)
        {
            if (string.IsNullOrEmpty(marketplaceId))
            {
                return "Unknown";
            }

            var provider = _marketplaceProviders?.FirstOrDefault(p =>
                string.Equals(p.Id, marketplaceId, StringComparison.OrdinalIgnoreCase));

            return provider?.DisplayName ?? marketplaceId;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allTemplates != null && _allTemplates.Count > 0)
            {
                ApplyFilters();
            }
        }

        private void OnProviderSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_allTemplates != null && _allTemplates.Count > 0)
            {
                ApplyFilters();

                string selectedName = _providerComboBox?.SelectedItem is MarketplaceAsProvider provider
                    ? provider.DisplayName
                    : _allMarketplacesText;
                SetStatus($"Showing {_filteredTemplates.Count} templates from {selectedName}");
            }
        }

        private async void OnRefreshButtonClick(object sender, RoutedEventArgs e)
        {
            await ExecuteUiActionAsync(() => LoadTemplatesAsync(forceRefresh: true), "Failed to refresh templates");
        }

        private void RenderTemplateListItems()
        {
            if (_templateTreeView == null)
            {
                return;
            }

            _templateTreeView.Items.Clear();
            var groups = _templateListItems
                .Where(item => item.IsGroupHeader)
                .ToList();

            foreach (var group in groups)
            {
                var groupItem = new TreeViewItem
                {
                    Header = CreateTemplateCheckBox(group),
                    IsExpanded = !IsTemplateGroupCollapsed(group.GroupKey),
                    Tag = group
                };

                groupItem.Expanded += OnTemplateGroupExpanded;
                groupItem.Collapsed += OnTemplateGroupCollapsed;

                var children = _templateListItems
                    .Where(item => !item.IsGroupHeader && string.Equals(item.GroupKey, group.GroupKey, StringComparison.OrdinalIgnoreCase));

                foreach (var child in children)
                {
                    groupItem.Items.Add(new TreeViewItem
                    {
                        Header = CreateTemplateCheckBox(child),
                        IsExpanded = false
                    });
                }

                _templateTreeView.Items.Add(groupItem);
            }
        }

        private bool IsTemplateGroupCollapsed(string groupKey)
        {
            return !string.IsNullOrWhiteSpace(groupKey)
                && _collapsedTemplateGroups.TryGetValue(groupKey, out var isCollapsed)
                && isCollapsed;
        }

        private void OnTemplateGroupExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem treeItem || treeItem.Tag is not TemplateListItemModel group)
            {
                return;
            }

            group.IsCollapsed = false;
            _collapsedTemplateGroups[group.GroupKey] = false;
        }

        private void OnTemplateGroupCollapsed(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem treeItem || treeItem.Tag is not TemplateListItemModel group)
            {
                return;
            }

            group.IsCollapsed = true;
            _collapsedTemplateGroups[group.GroupKey] = true;
        }

        private CheckBox CreateTemplateCheckBox(TemplateListItemModel item)
        {
            var checkBox = new CheckBox
            {
                IsChecked = item.IsChecked,
                Tag = item,
                Margin = item.IsGroupHeader ? new Thickness(0, 6, 0, 0) : new Thickness(16, 1, 0, 1),
                FontWeight = item.IsGroupHeader ? FontWeights.Bold : FontWeights.Normal,
                Opacity = item.IsGroupHeader ? 0.9 : 1.0,
                ToolTip = item.IsGroupHeader ? null : GetTemplateTooltip(item)
            };

            checkBox.SetResourceReference(CheckBox.StyleProperty, VsResourceKeys.CheckBoxStyleKey);
            checkBox.Checked += OnTemplateItemCheckedChanged;
            checkBox.Unchecked += OnTemplateItemCheckedChanged;

            if (item.IsGroupHeader)
            {
                checkBox.Content = item.GroupDisplayName;
            }
            else if (!string.IsNullOrEmpty(item.Category))
            {
                checkBox.Content = $"{item.Name} ({item.Category})";
            }
            else
            {
                checkBox.Content = item.Name;
            }

            return checkBox;
        }

        private ToolTip GetTemplateTooltip(TemplateListItemModel item)
        {
            if (item == null || item.IsGroupHeader || item.Template == null)
            {
                return null;
            }

            if (_templateTooltipCache.TryGetValue(item.Template, out var tooltip))
            {
                return tooltip;
            }

            var title = item.Template.DisplayName ?? item.Template.Name ?? item.Template.FileName ?? "Template";
            var description = item.Template.Description;
            if (string.IsNullOrWhiteSpace(description))
            {
                description = string.IsNullOrWhiteSpace(item.Template.Category)
                    ? "No description available."
                    : item.Template.Category;
            }

            var stack = new StackPanel
            {
                MaxWidth = 250
            };

            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = description,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            tooltip = new ToolTip
            {
                Content = stack,
                MaxWidth = 250
            };

            _templateTooltipCache[item.Template] = tooltip;
            return tooltip;
        }

        private void OnTemplateItemCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingTemplateChecks || sender is not CheckBox checkBox || checkBox.Tag is not TemplateListItemModel item)
            {
                return;
            }

            var isChecked = checkBox.IsChecked == true;
            _isUpdatingTemplateChecks = true;

            try
            {
                if (item.IsGroupHeader)
                {
                    foreach (var child in _templateListItems.Where(candidate => !candidate.IsGroupHeader && string.Equals(candidate.GroupKey, item.GroupKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        child.IsChecked = isChecked;
                        UpdateSelectedTemplateKey(child.Template, isChecked);
                    }

                    item.IsChecked = isChecked;
                }
                else
                {
                    item.IsChecked = isChecked;
                    UpdateSelectedTemplateKey(item.Template, isChecked);
                    UpdateGroupHeaderCheckState(item.GroupKey);
                }

                UpdateTemplateLabelSelectionCount();
                CollectSelectedTemplates();
            }
            finally
            {
                _isUpdatingTemplateChecks = false;
            }

            RenderTemplateListItems();
        }

        private void UpdateGroupHeaderCheckState(string groupKey)
        {
            var header = _templateListItems.FirstOrDefault(item => item.IsGroupHeader && string.Equals(item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase));
            if (header == null)
            {
                return;
            }

            var children = _templateListItems
                .Where(item => !item.IsGroupHeader && string.Equals(item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            header.IsChecked = children.Count > 0 && children.All(item => item.IsChecked);
        }

        private void UpdateTemplateLabelSelectionCount()
        {
            if (_templateLabel == null)
            {
                return;
            }

            var selectedCount = _templateListItems.Count(item => !item.IsGroupHeader && item.IsChecked);
            var totalSelectedCount = _selectedTemplateKeys.Count;
            _templateLabel.Text = totalSelectedCount > 0
                ? $"Templates ({totalSelectedCount} selected):"
                : "Templates:";

            if (_templateType != null && _statusText != null && _allTemplates.Count > 0)
            {
                if (totalSelectedCount == 0)
                {
                    _statusText.Text = "No template selected. Enter a file name to create a scaffolded file.";
                }
                else
                {
                    _statusText.Text = $"{totalSelectedCount} template(s) selected.";
                }
            }

            UpdateFileNameInputState();
        }

        private void UpdateFileNameInputState()
        {
            if (_templateType == null || _textBox == null)
            {
                return;
            }

            var hasCheckedTemplates = _selectedTemplateKeys.Count > 0;
            _textBox.IsReadOnly = hasCheckedTemplates;
        }

        private void ApplyPreselectedTemplates()
        {
            if (_hasAppliedPreselectedTemplates || _preselectedTemplateFileNames.Count == 0)
            {
                return;
            }

            foreach (var template in _allTemplates.Where(template => TemplateSelectionKey.MatchesFileName(template, _preselectedTemplateFileNames)))
            {
                _selectedTemplateKeys.Add(TemplateSelectionKey.Create(template));
            }

            _hasAppliedPreselectedTemplates = true;
        }

        private void UpdateSelectedTemplateKey(TemplateInfo template, bool isSelected)
        {
            var key = TemplateSelectionKey.Create(template);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (isSelected)
            {
                _selectedTemplateKeys.Add(key);
            }
            else
            {
                _selectedTemplateKeys.Remove(key);
            }
        }

        private void CollectSelectedTemplates()
        {
            if (_templateType == null)
            {
                SelectedTemplates = [];
                return;
            }

            var selected = _allTemplates
                .Where(template => _selectedTemplateKeys.Contains(TemplateSelectionKey.Create(template)))
                .Select(template =>
                {
                    var content = template.Content;
                    if (string.IsNullOrEmpty(content))
                    {
                        content = MarketplaceTemplateAdapter.GetTemplateContent(template);
                        template.Content = content;
                    }

                    return new TemplateSelectionResult
                    {
                        Template = template,
                        FileName = template.FileName,
                        Content = content
                    };
                })
                .Where(item => !string.IsNullOrEmpty(item.Content))
                .ToList();

            SelectedTemplates = selected;
            SelectedTemplateContent = selected.Count == 1 ? selected[0].Content : null;
        }

        private bool TryFinalizeSelection()
        {
            CollectSelectedTemplates();

            if (_templateType != null && SelectedTemplates.Count > 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(_textBox?.Text))
            {
                return true;
            }

            _ = VS.MessageBox.ShowWarningAsync("Invalid Input", "Please enter a file name or select one or more templates.");
            return false;
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.Text = message;
            }
        }

        private void SetRefreshEnabled(bool enabled)
        {
            if (_refreshButton != null)
            {
                _refreshButton.IsEnabled = enabled;
            }
        }

        private void SetInputControlsEnabled(bool enabled)
        {
            if (_userProfileCheckBox != null)
            {
                _userProfileCheckBox.IsEnabled = enabled;
            }

            if (_providerComboBox != null)
            {
                _providerComboBox.IsEnabled = enabled;
            }

            if (_searchBox != null)
            {
                _searchBox.IsEnabled = enabled;
            }

            if (_templateTreeView != null)
            {
                _templateTreeView.IsEnabled = enabled;
            }

            if (_textBox != null)
            {
                _textBox.IsEnabled = enabled;
                UpdateFileNameInputState();
            }
        }

        private void OnFileNameTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_templateType == null)
            {
                UpdatePreview();
            }
        }

        private async Task ExecuteUiActionAsync(Func<Task> action, string statusOnError = null)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                if (!string.IsNullOrWhiteSpace(statusOnError))
                {
                    SetStatus(statusOnError);
                }
            }
        }

        private static CancellationTokenSource ReplaceCancellationTokenSource(ref CancellationTokenSource tokenSource)
        {
            CancelAndDispose(ref tokenSource);
            tokenSource = new CancellationTokenSource();
            return tokenSource;
        }

        private static void CancelAndDispose(ref CancellationTokenSource tokenSource)
        {
            if (tokenSource == null)
            {
                return;
            }

            try
            {
                tokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                tokenSource.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            tokenSource = null;
        }

        private void ClearPreviewContent()
        {
            if (_previewBox == null)
            {
                return;
            }

            _previewBox.Document.Blocks.Clear();
        }

        private void UpdatePreview()
        {
            if (_previewBox == null)
            {
                return;
            }

            if (_previewGenerator == null)
            {
                ClearPreviewContent();
                return;
            }

            try
            {
                string preview = _previewGenerator(_textBox.Text);
                UpdatePreviewWithContent(preview);
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                _previewBox.Document.Blocks.Clear();
                _previewBox.Document.Blocks.Add(new Paragraph(new Run("(Preview unavailable)")));
            }
        }

        private void UpdatePreviewWithContent(string content)
        {
            if (_previewBox == null || content == null)
            {
                return;
            }

            string[] lines = content.Split('\n');
            string displayContent;
            var truncated = false;

            if (lines.Length > 50)
            {
                displayContent = string.Join("\n", lines, 0, 50);
                truncated = true;
            }
            else
            {
                displayContent = content;
            }

            _previewBox.Document = MarkdownSyntaxHighlighter.CreateHighlightedDocument(displayContent, truncated);
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

        private MarketplaceAsProvider GetSelectedProvider()
        {
            if (_providerComboBox?.SelectedItem is MarketplaceAsProvider provider)
            {
                return provider;
            }

            if (_marketplaceProviders.Count > 0)
            {
                return _marketplaceProviders[0];
            }

            return null;
        }

        private string GetTemplateSourceSummary(TemplateInfo template)
        {
            if (template == null)
            {
                return "Template";
            }

            MarketplaceAsProvider provider = null;
            if (!string.IsNullOrWhiteSpace(template.ProviderId))
            {
                provider = _marketplaceProviders.Find(candidate => candidate.Id == template.ProviderId);
            }

            provider ??= GetSelectedProvider();

            var templateName = string.IsNullOrWhiteSpace(template.DisplayName)
                ? (string.IsNullOrWhiteSpace(template.Name) ? template.FileName : template.Name)
                : template.DisplayName;

            if (provider == null)
            {
                return string.IsNullOrWhiteSpace(templateName)
                    ? "Template"
                    : $"Template: {templateName}";
            }

            if (string.IsNullOrWhiteSpace(templateName))
            {
                return $"Source: {provider.DisplayName}";
            }

            return $"Template: {templateName} - Source: {provider.DisplayName}";
        }
    }

    /// <summary>
    /// Display item for template dropdown with separate name and category for styling.
    /// </summary>
    internal sealed class TemplateDisplayItem
    {
        public string Name { get; }
        public string Category { get; }
        public TemplateInfo Template { get; }

        public TemplateDisplayItem(TemplateInfo template)
        {
            Template = template;
            Name = template?.DisplayName ?? template?.Name ?? template?.FileName ?? "Unknown";
            Category = template?.Category;
        }

        public override string ToString() => string.IsNullOrEmpty(Category)
            ? Name
            : $"{Name} ({Category})";
    }

    internal sealed class TemplateSelectionResult
    {
        public TemplateInfo Template { get; init; }

        public string FileName { get; init; }

        public string Content { get; init; }
    }

    internal sealed class TemplateListItemModel
    {
        public bool IsGroupHeader { get; set; }

        public bool IsCollapsed { get; set; }

        public string GroupKey { get; set; }

        public string GroupDisplayName { get; set; }

        public TemplateInfo Template { get; set; }

        public string Name => IsGroupHeader
            ? GroupDisplayName
            : Template?.DisplayName ?? Template?.Name ?? Template?.FileName ?? "Unknown";

        public string Category => Template?.Category;

        public bool IsChecked { get; set; }

        public string Tooltip { get; set; }
    }

    internal static class TemplateSelectionKey
    {
        public static string Create(TemplateInfo template)
        {
            if (template == null)
            {
                return null;
            }

            var providerId = template.ProviderId ?? string.Empty;
            var identity = template.FileName ?? template.DownloadUrl ?? template.Name ?? template.DisplayName ?? string.Empty;
            return $"{providerId}|{template.TemplateType}|{identity}";
        }

        public static bool MatchesFileName(TemplateInfo template, ISet<string> fileNames)
        {
            return template != null
                && fileNames != null
                && !string.IsNullOrWhiteSpace(template.FileName)
                && fileNames.Contains(template.FileName);
        }
    }
}

