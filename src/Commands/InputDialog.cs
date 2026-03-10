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
        private const string _customTemplateText = "<Custom>";
        private const string _allMarketplacesText = "All Marketplaces";
        private const string _settingsKey = "InputDialog";

        private readonly TextBox _textBox;
        private readonly RichTextBox _previewBox;
        private readonly ComboBox _providerComboBox;
        private readonly TextBox _searchBox;
        private readonly ComboBox _templateComboBox;
        private readonly TextBlock _templateLabel;
        private readonly TextBlock _statusText;
        private readonly Button _refreshButton;
        private readonly Button _copyButton;
        private readonly Func<string, string> _previewGenerator;
        private readonly TemplateType? _templateType;
        private readonly string _defaultFileName;

        private List<MarketplaceAsProvider> _marketplaceProviders;
        private List<TemplateInfo> _allTemplates;
        private List<TemplateInfo> _filteredTemplates;
        private bool _userModifiedFileName;
        private string _currentPreviewContent;
        private CancellationTokenSource _templateListCancellationTokenSource;
        private CancellationTokenSource _templateContentCancellationTokenSource;

        /// <summary>
        /// Gets the text entered by the user.
        /// </summary>
        public string InputText => _textBox.Text;

        /// <summary>
        /// Gets the content to use for the file.
        /// Returns the selected template content, or null if using custom/default template.
        /// </summary>
        public string SelectedTemplateContent { get; private set; }

        /// <summary>
        /// Creates a new input dialog.
        /// </summary>
        public InputDialog(
            string title,
            string prompt,
            string defaultValue = "",
            Func<string, string> previewGenerator = null,
            TemplateType? templateType = null,
            IReadOnlyList<MarketplaceAsProvider> marketplaceProviders = null)
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

            var grid = new Grid
            {
                Margin = new Thickness(12)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt

            // Always add rows for marketplace, search, and template when templateType is set
            if (templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Marketplace label
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Marketplace dropdown
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search label
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search box
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Template label
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Template dropdown
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // File name label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // File name textbox

            if (previewGenerator != null || templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            if (templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var currentRow = 0;

            var label = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(label, currentRow++);
            grid.Children.Add(label);

            if (templateType != null)
            {
                // Marketplace dropdown (always shown for templates)
                var providerLabel = new TextBlock
                {
                    Text = "Marketplace:",
                    Margin = new Thickness(0, 0, 0, 4)
                };
                providerLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(providerLabel, currentRow++);
                grid.Children.Add(providerLabel);

                _providerComboBox = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    IsEditable = false
                };
                _providerComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
                _providerComboBox.Items.Add(_allMarketplacesText);
                _providerComboBox.SelectedIndex = 0;
                _providerComboBox.SelectionChanged += OnProviderSelectionChanged;
                AutomationProperties.SetName(_providerComboBox, "Marketplace filter");
                AutomationProperties.SetHelpText(_providerComboBox, "Filter templates by marketplace, or select 'All Marketplaces' to see all.");
                Grid.SetRow(_providerComboBox, currentRow++);
                grid.Children.Add(_providerComboBox);

                // Search box
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
                AutomationProperties.SetName(_searchBox, "Search templates");
                AutomationProperties.SetHelpText(_searchBox, "Type to filter templates by name.");
                Grid.SetRow(_searchBox, currentRow++);
                grid.Children.Add(_searchBox);

                // Template dropdown
                _templateLabel = new TextBlock
                {
                    Text = "Template:",
                    Margin = new Thickness(0, 0, 0, 4)
                };
                _templateLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(_templateLabel, currentRow++);
                grid.Children.Add(_templateLabel);

                _templateComboBox = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    IsEditable = false,
                    ItemTemplate = CreateTemplateItemTemplate()
                };
                _templateComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
                _templateComboBox.Items.Add(_customTemplateText);
                _templateComboBox.SelectedIndex = 0;
                _templateComboBox.SelectionChanged += OnTemplateSelectionChanged;
                AutomationProperties.SetName(_templateComboBox, "Template selection");
                AutomationProperties.SetHelpText(_templateComboBox, "Use Alt + Up or Alt + Down to move between templates.");
                Grid.SetRow(_templateComboBox, currentRow++);
                grid.Children.Add(_templateComboBox);
            }

            var fileNameLabel = new TextBlock
            {
                Text = "File name:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            fileNameLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(fileNameLabel, currentRow++);
            grid.Children.Add(fileNameLabel);

            _textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(4, 2, 4, 2)
            };
            _textBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _textBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _textBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            _textBox.TextChanged += OnFileNameTextChanged;
            AutomationProperties.SetName(_textBox, "File name");
            AutomationProperties.SetHelpText(_textBox, "Enter the name of the file to create.");
            Grid.SetRow(_textBox, currentRow++);
            grid.Children.Add(_textBox);

            if (previewGenerator != null || templateType != null)
            {
                var previewLabel = new TextBlock
                {
                    Text = "Preview:",
                    Margin = new Thickness(0, 0, 0, 4)
                };
                previewLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(previewLabel, currentRow++);
                grid.Children.Add(previewLabel);

                _previewBox = new RichTextBox
                {
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(4),
                    BorderThickness = new Thickness(1)
                };
                _previewBox.SetResourceReference(RichTextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
                _previewBox.SetResourceReference(RichTextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
                _previewBox.SetResourceReference(RichTextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
                AutomationProperties.SetName(_previewBox, "Template preview");
                AutomationProperties.SetHelpText(_previewBox, "Read-only preview of the selected template content.");

                Grid.SetRow(_previewBox, currentRow++);
                grid.Children.Add(_previewBox);
            }

            if (templateType != null)
            {
                _statusText = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                _statusText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                AutomationProperties.SetName(_statusText, "Template status");

                Grid.SetRow(_statusText, currentRow++);
                grid.Children.Add(_statusText);
            }

            var buttonRowGrid = new Grid();
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(buttonRowGrid, currentRow);

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

                _copyButton = new Button
                {
                    Content = "Copy",
                    Width = 40,
                    Height = 20,
                    Padding = new Thickness(0),
                    FontSize = 10,
                    ToolTip = "Copy preview content to clipboard (Ctrl+Shift+C)",
                    Margin = new Thickness(0, 0, 8, 0)
                };
                _copyButton.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
                _copyButton.Click += OnCopyButtonClick;
                AutomationProperties.SetName(_copyButton, "Copy template preview");
                AutomationProperties.SetHelpText(_copyButton, "Copy the full preview text to the clipboard.");
                actionPanel.Children.Add(_copyButton);

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

            Loaded += OnDialogLoaded;
            SourceInitialized += OnSourceInitialized;
            Closing += OnDialogClosing;
            KeyDown += OnKeyDown;

            RestoreDialogSize();
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
                return;
            }

            if (_templateComboBox != null && _templateComboBox.Items.Count > 0)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    if (e.Key == Key.Up)
                    {
                        MoveTemplateSelection(-1);
                        e.Handled = true;
                        return;
                    }

                    if (e.Key == Key.Down)
                    {
                        MoveTemplateSelection(1);
                        e.Handled = true;
                        return;
                    }
                }
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)
                && e.Key == Key.C
                && _copyButton != null
                && _copyButton.IsEnabled)
            {
                OnCopyButtonClick(_copyButton, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void MoveTemplateSelection(int offset)
        {
            if (_templateComboBox == null || _templateComboBox.Items.Count == 0)
            {
                return;
            }

            var nextIndex = _templateComboBox.SelectedIndex + offset;
            if (nextIndex < 0)
            {
                nextIndex = 0;
            }
            else if (nextIndex >= _templateComboBox.Items.Count)
            {
                nextIndex = _templateComboBox.Items.Count - 1;
            }

            if (nextIndex != _templateComboBox.SelectedIndex)
            {
                _templateComboBox.SelectedIndex = nextIndex;
            }
        }

        private async void OnDialogLoaded(object sender, RoutedEventArgs e)
        {
            _textBox.Focus();
            _textBox.SelectAll();
            UpdatePreview();

            if (_templateType != null && _templateComboBox != null)
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

                    _marketplaceProviders = await TemplateProviderRegistry.GetProvidersForTemplateTypeAsync(_templateType.Value, cancellationToken);
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

                // Load all templates from all providers
                _allTemplates = new List<TemplateInfo>();
                foreach (var provider in _marketplaceProviders)
                {
                    var templates = await TemplateProviderRegistry.GetTemplatesAsync(_templateType.Value, provider, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    _allTemplates.AddRange(templates);
                }

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
                    currentRequestCancellation.Dispose();
                }
            }
        }

        private void ApplyFilters()
        {
            // Get selected marketplace filter
            string selectedMarketplaceId = null;
            if (_providerComboBox?.SelectedItem is MarketplaceAsProvider selectedProvider)
            {
                selectedMarketplaceId = selectedProvider.Id;
            }

            // Get search text
            string searchText = _searchBox?.Text?.Trim() ?? "";

            // Filter templates
            _filteredTemplates = _allTemplates
                .Where(t =>
                {
                    // Filter by marketplace
                    if (selectedMarketplaceId != null && !string.Equals(t.ProviderId, selectedMarketplaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    // Filter by search text
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        var name = t.DisplayName ?? t.Name ?? t.FileName ?? "";
                        if (name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .ToList();

            // Update template dropdown (grouped when showing all marketplaces)
            bool showGrouped = selectedMarketplaceId == null;
            UpdateTemplateDropdown(showGrouped);
        }

        private void UpdateTemplateDropdown(bool groupByMarketplace)
        {
            if (_templateComboBox == null)
            {
                return;
            }

            _templateComboBox.Items.Clear();
            _templateComboBox.Items.Add(_customTemplateText);

            if (groupByMarketplace && _filteredTemplates.Count > 0)
            {
                // Build a lookup for marketplace ordering (preserve the order from provider list)
                var marketplaceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _marketplaceProviders.Count; i++)
                {
                    var providerId = _marketplaceProviders[i].Id;
                    if (!string.IsNullOrEmpty(providerId) && !marketplaceOrder.ContainsKey(providerId))
                    {
                        marketplaceOrder[providerId] = i;
                    }
                }

                // Group by marketplace and preserve original marketplace order
                var grouped = _filteredTemplates
                    .GroupBy(t => t.ProviderId ?? "unknown")
                    .OrderBy(g => marketplaceOrder.TryGetValue(g.Key, out int order) ? order : int.MaxValue);

                foreach (var group in grouped)
                {
                    var marketplaceName = GetMarketplaceDisplayName(group.Key);

                    // Add group header (disabled separator item)
                    var header = new ComboBoxItem
                    {
                        Content = $"── {marketplaceName} ──",
                        IsEnabled = false,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Colors.Gray)
                    };
                    _templateComboBox.Items.Add(header);

                    // Add templates in this group, sorted by name
                    foreach (var template in group.OrderBy(t => t.DisplayName ?? t.Name ?? t.FileName))
                    {
                        _templateComboBox.Items.Add(new TemplateDisplayItem(template));
                    }
                }
            }
            else
            {
                // Simple list sorted by name
                foreach (var template in _filteredTemplates.OrderBy(t => t.DisplayName ?? t.Name ?? t.FileName))
                {
                    _templateComboBox.Items.Add(new TemplateDisplayItem(template));
                }
            }

            _templateComboBox.SelectedIndex = 0;
            _templateLabel.Text = $"Template ({_filteredTemplates.Count} available):";
        }

        /// <summary>
        /// Creates a DataTemplate for template dropdown items with name and category styling.
        /// </summary>
        private static DataTemplate CreateTemplateItemTemplate()
        {
            var template = new DataTemplate();

            // Use DockPanel for simpler layout: category docked right, name fills remaining space
            var factory = new FrameworkElementFactory(typeof(DockPanel));
            factory.SetValue(DockPanel.LastChildFillProperty, true);

            // Category TextBlock (dimmed, docked right)
            var categoryBlock = new FrameworkElementFactory(typeof(TextBlock));
            categoryBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Category"));
            categoryBlock.SetValue(TextBlock.OpacityProperty, 0.6);
            categoryBlock.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 0, 0));
            categoryBlock.SetValue(TextBlock.FontSizeProperty, 11.0);
            categoryBlock.SetValue(DockPanel.DockProperty, Dock.Right);

            // Name TextBlock (fills remaining space)
            var nameBlock = new FrameworkElementFactory(typeof(TextBlock));
            nameBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            nameBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            // Add category first (docked right), then name (fills)
            factory.AppendChild(categoryBlock);
            factory.AppendChild(nameBlock);

            template.VisualTree = factory;
            return template;
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

        /// <summary>
        /// Gets the template for the currently selected dropdown item, accounting for group headers.
        /// </summary>
        private TemplateInfo GetSelectedTemplate()
        {
            if (_templateComboBox == null || _templateComboBox.SelectedIndex <= 0)
            {
                return null;
            }

            var selectedItem = _templateComboBox.SelectedItem;

            // If it's a ComboBoxItem (header), return null
            if (selectedItem is ComboBoxItem)
            {
                return null;
            }

            // If it's a TemplateDisplayItem, return the associated template
            if (selectedItem is TemplateDisplayItem displayItem)
            {
                return displayItem.Template;
            }

            // It's a string (e.g., "<Custom>") - return null
            return null;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            // Apply filters when search text changes
            if (_allTemplates != null && _allTemplates.Count > 0)
            {
                ApplyFilters();
            }
        }

        private void OnProviderSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Apply filters when marketplace selection changes
            if (_allTemplates != null && _allTemplates.Count > 0)
            {
                ApplyFilters();

                // Update status with filter info
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

        private void OnCopyButtonClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentPreviewContent))
            {
                try
                {
                    Clipboard.SetText(_currentPreviewContent);
                    SetStatus("Copied to clipboard");
                }
                catch (Exception ex)
                {
                    _ = ex.LogAsync();
                    SetStatus("Failed to copy");
                }
            }
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

        private void OnFileNameTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_templateComboBox != null && _templateComboBox.SelectedIndex > 0)
            {
                TemplateInfo selectedTemplate = GetSelectedTemplate();
                if (selectedTemplate != null && _textBox.Text != selectedTemplate.FileName)
                {
                    _userModifiedFileName = true;
                }
            }
            else if (_textBox.Text != _defaultFileName)
            {
                _userModifiedFileName = true;
            }

            if (_templateComboBox == null || _templateComboBox.SelectedIndex == 0)
            {
                UpdatePreview();
            }
        }

        private async void OnTemplateSelectionChanged(object sender, RoutedEventArgs e)
        {
            // Skip if a header item was selected
            if (_templateComboBox?.SelectedItem is ComboBoxItem)
            {
                // Move to next non-header item
                var currentIndex = _templateComboBox.SelectedIndex;
                for (int i = currentIndex + 1; i < _templateComboBox.Items.Count; i++)
                {
                    if (!(_templateComboBox.Items[i] is ComboBoxItem))
                    {
                        _templateComboBox.SelectedIndex = i;
                        return;
                    }
                }

                // No valid item found, go back to Custom
                _templateComboBox.SelectedIndex = 0;
                return;
            }

            await ExecuteUiActionAsync(OnTemplateSelectionChangedAsync, "Failed to load template content");
        }

        private async Task OnTemplateSelectionChangedAsync()
        {
            CancellationTokenSource currentRequestCancellation = ReplaceCancellationTokenSource(ref _templateContentCancellationTokenSource);
            CancellationToken cancellationToken = currentRequestCancellation.Token;

            try
            {
                if (_templateComboBox.SelectedIndex == 0)
                {
                    SelectedTemplateContent = null;
                    if (!_userModifiedFileName)
                    {
                        _textBox.Text = _defaultFileName;
                    }

                    UpdatePreview();
                    SetStatus("Custom template");
                    return;
                }

                TemplateInfo template = GetSelectedTemplate();
                if (template == null)
                {
                    return;
                }

                if (!_userModifiedFileName)
                {
                    _textBox.Text = template.FileName;
                }

                if (string.IsNullOrEmpty(template.Content))
                {
                    SetStatus("Loading template content...");

                    // For marketplace templates, content is loaded from local files
                    await Task.Run(() =>
                    {
                        template.Content = TemplateProviderRegistry.GetTemplateContent(template);
                    }, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(template.Content))
                    {
                        SelectedTemplateContent = null;
                        SetStatus("Failed to load template content.");
                        ClearPreviewContent();
                        return;
                    }
                }

                SelectedTemplateContent = template.Content;
                UpdatePreviewWithContent(template.Content);

                var sizeKb = (template.Content?.Length ?? 0) / 1024.0;
                var sizeText = sizeKb >= 1.0 ? $"{sizeKb:F1} KB" : $"{template.Content?.Length ?? 0} bytes";
                SetStatus($"{GetTemplateSourceSummary(template)} - {sizeText}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (ReferenceEquals(_templateContentCancellationTokenSource, currentRequestCancellation))
                {
                    _templateContentCancellationTokenSource = null;
                    currentRequestCancellation.Dispose();
                }
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
            _currentPreviewContent = null;
            SetCopyEnabled(false);
        }

        private void UpdatePreview()
        {
            if (_previewBox == null)
            {
                return;
            }

            if (_templateComboBox != null && _templateComboBox.SelectedIndex > 0)
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
                _currentPreviewContent = null;
                SetCopyEnabled(false);
            }
        }

        private void UpdatePreviewWithContent(string content)
        {
            if (_previewBox == null || content == null)
            {
                return;
            }

            _currentPreviewContent = content;
            SetCopyEnabled(true);

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

        private void SetCopyEnabled(bool enabled)
        {
            if (_copyButton != null)
            {
                _copyButton.IsEnabled = enabled;
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
}

