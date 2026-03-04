using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GitHubNode.Services;
using Microsoft.VisualStudio.PlatformUI;
using System.Runtime.InteropServices;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A simple input dialog for prompting the user for text input.
    /// Uses Visual Studio theming for consistent appearance.
    /// Supports optional template dropdown loaded from remote repositories.
    /// </summary>
    internal sealed class InputDialog : DialogWindow
    {
        private const int _dwmwaUseImmersiveDarkMode = 20;
        private const int _dwmwaCaptionColor = 35;
        private const int _dwmwaTextColor = 36;
        private const string _customTemplateText = "<Custom>";
        private const string _settingsKey = "InputDialog";

        private readonly TextBox _textBox;
        private readonly RichTextBox _previewBox;
        private readonly ComboBox _providerComboBox;
        private readonly ComboBox _templateComboBox;
        private readonly TextBlock _templateLabel;
        private readonly TextBlock _statusText;
        private readonly Button _refreshButton;
        private readonly Button _copyButton;
        private readonly Func<string, string> _previewGenerator;
        private readonly TemplateType? _templateType;
        private readonly List<TemplateProvider> _templateProviders;
        private readonly string _defaultFileName;

        private bool _userModifiedFileName;
        private List<TemplateInfo> _templates;
        private string _currentPreviewContent;
        private int _templateListRequestVersion;
        private int _templateContentRequestVersion;

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
            IReadOnlyList<TemplateProvider> templateProviders = null)
        {
            _previewGenerator = previewGenerator;
            _templateType = templateType;
            _templateProviders = templateProviders == null
                ? new List<TemplateProvider>()
                : new List<TemplateProvider>(templateProviders);
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

            var grid = new Grid
            {
                Margin = new Thickness(12)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var showProviderDropdown = templateType != null && _templateProviders.Count > 1;
            if (showProviderDropdown)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            if (templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            if (previewGenerator != null || templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
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

            if (showProviderDropdown)
            {
                var providerLabel = new TextBlock
                {
                    Text = "Provider:",
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

                foreach (TemplateProvider provider in _templateProviders)
                {
                    _providerComboBox.Items.Add(provider);
                }

                _providerComboBox.SelectedIndex = 0;
                _providerComboBox.SelectionChanged += OnProviderSelectionChanged;

                Grid.SetRow(_providerComboBox, currentRow++);
                grid.Children.Add(_providerComboBox);
            }

            if (templateType != null)
            {
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
                    IsEditable = false
                };
                _templateComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
                _templateComboBox.Items.Add(_customTemplateText);
                _templateComboBox.SelectedIndex = 0;
                _templateComboBox.SelectionChanged += (s, e) => OnTemplateSelectionChanged();

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

                Grid.SetRow(_previewBox, currentRow++);
                grid.Children.Add(_previewBox);
            }

            var buttonRowGrid = new Grid();
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(buttonRowGrid, currentRow);

            if (templateType != null)
            {
                var statusPanel = new StackPanel
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
                statusPanel.Children.Add(_refreshButton);

                _copyButton = new Button
                {
                    Content = "Copy",
                    Width = 40,
                    Height = 20,
                    Padding = new Thickness(0),
                    FontSize = 10,
                    ToolTip = "Copy preview content to clipboard",
                    Margin = new Thickness(0, 0, 8, 0)
                };
                _copyButton.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
                _copyButton.Click += OnCopyButtonClick;
                statusPanel.Children.Add(_copyButton);

                _statusText = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11
                };
                _statusText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                statusPanel.Children.Add(_statusText);

                Grid.SetColumn(statusPanel, 0);
                buttonRowGrid.Children.Add(statusPanel);
            }

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button okButton = CreateThemedButton("OK", isDefault: true);
            okButton.Margin = new Thickness(0, 0, 8, 0);
            okButton.Click += (s, e) =>
            {
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(okButton);

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
            => SaveDialogSize();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5 && _refreshButton != null && _refreshButton.IsEnabled)
            {
                OnRefreshButtonClick(_refreshButton, new RoutedEventArgs());
                e.Handled = true;
            }
        }

#pragma warning disable VSTHRD100
        private async void OnDialogLoaded(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
        {
            _textBox.Focus();
            _textBox.SelectAll();
            UpdatePreview();

            if (_templateType != null && _templateComboBox != null)
            {
                try
                {
                    await LoadTemplatesAsync();
                }
                catch (Exception ex)
                {
                    _ = ex.LogAsync();
                }
            }
        }

        private async Task LoadTemplatesAsync(bool forceRefresh = false)
        {
            var requestVersion = ++_templateListRequestVersion;

            try
            {
                TemplateProvider provider = GetSelectedProvider();
                if (provider == null)
                {
                    SetStatus("No providers available");
                    return;
                }

                SetStatus("Fetching templates from GitHub...");
                SetRefreshEnabled(false);

                if (forceRefresh)
                {
                    AwesomeCopilotService.ClearCache(_templateType.Value, provider);
                }

                _templates = await AwesomeCopilotService.GetTemplatesAsync(_templateType.Value, provider);
                if (requestVersion != _templateListRequestVersion)
                {
                    return;
                }

                while (_templateComboBox.Items.Count > 1)
                {
                    _templateComboBox.Items.RemoveAt(1);
                }

                foreach (TemplateInfo template in _templates)
                {
                    _templateComboBox.Items.Add(template.DisplayName ?? template.FileName);
                }

                if (_templates.Count > 0)
                {
                    SetStatus($"Loaded {_templates.Count} templates");
                    _templateLabel.Text = $"Template ({_templates.Count} available):";
                }
                else
                {
                    string fetchIssue = AwesomeCopilotService.GetLastFetchIssue(_templateType.Value, provider);
                    SetStatus(!string.IsNullOrWhiteSpace(fetchIssue)
                        ? fetchIssue
                        : "No templates available (offline?)");
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();

                while (_templateComboBox.Items.Count > 1)
                {
                    _templateComboBox.Items.RemoveAt(1);
                }

                SetStatus("Failed to load templates");
            }
            finally
            {
                if (requestVersion == _templateListRequestVersion)
                {
                    SetRefreshEnabled(true);
                }
            }
        }

#pragma warning disable VSTHRD100
        private async void OnProviderSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
#pragma warning restore VSTHRD100
        {
            try
            {
                _userModifiedFileName = false;
                _templateComboBox.SelectedIndex = 0;
                SelectedTemplateContent = null;
                UpdatePreview();
                await LoadTemplatesAsync();
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                SetStatus("Failed to load provider templates");
            }
        }

#pragma warning disable VSTHRD100
        private async void OnRefreshButtonClick(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
        {
            try
            {
                await LoadTemplatesAsync(forceRefresh: true);
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
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
                TemplateInfo selectedTemplate = _templates?[_templateComboBox.SelectedIndex - 1];
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

#pragma warning disable VSTHRD100
        private async void OnTemplateSelectionChanged()
#pragma warning restore VSTHRD100
        {
            try
            {
                if (_templateComboBox.SelectedIndex == 0)
                {
                    _templateContentRequestVersion++;
                    SelectedTemplateContent = null;
                    if (!_userModifiedFileName)
                    {
                        _textBox.Text = _defaultFileName;
                    }

                    UpdatePreview();
                    SetStatus("");
                    return;
                }

                if (_templates == null || _templateComboBox.SelectedIndex <= 0)
                {
                    return;
                }

                TemplateInfo template = _templates[_templateComboBox.SelectedIndex - 1];

                if (!_userModifiedFileName)
                {
                    _textBox.Text = template.FileName;
                }

                var requestVersion = ++_templateContentRequestVersion;

                if (string.IsNullOrEmpty(template.Content))
                {
                    SetStatus("Loading template content...");
                    template.Content = await AwesomeCopilotService.GetTemplateContentAsync(template);
                }

                if (requestVersion != _templateContentRequestVersion)
                {
                    return;
                }

                SelectedTemplateContent = template.Content;
                UpdatePreviewWithContent(template.Content);

                var sizeKb = (template.Content?.Length ?? 0) / 1024.0;
                SetStatus(sizeKb >= 1.0 ? $"{sizeKb:F1} KB" : $"{template.Content?.Length ?? 0} bytes");
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                SetStatus("Failed to load template content");
            }
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
                _previewBox.Document.Blocks.Clear();
                _currentPreviewContent = null;
                SetCopyEnabled(false);
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

        private TemplateProvider GetSelectedProvider()
        {
            if (_providerComboBox?.SelectedItem is TemplateProvider provider)
            {
                return provider;
            }

            if (_templateProviders.Count > 0)
            {
                return _templateProviders[0];
            }

            return null;
        }
    }
}

