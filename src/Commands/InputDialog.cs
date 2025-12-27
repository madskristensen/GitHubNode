using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GitHubNode.Services;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A simple input dialog for prompting the user for text input.
    /// Uses Visual Studio theming for consistent appearance.
    /// Supports optional template dropdown loaded from awesome-copilot repository.
    /// </summary>
    internal sealed class InputDialog : DialogWindow
    {
        private const string _customTemplateText = "<Custom>";

        private readonly TextBox _textBox;
        private readonly TextBox _previewBox;
        private readonly ComboBox _templateComboBox;
        private readonly TextBlock _statusText;
        private readonly Button _refreshButton;
        private readonly Func<string, string> _previewGenerator;
        private readonly TemplateType? _templateType;
        private readonly string _defaultFileName;
        private bool _userModifiedFileName;
        private List<TemplateInfo> _templates;

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
        /// <param name="title">The dialog title.</param>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="defaultValue">The default value in the text box.</param>
        /// <param name="previewGenerator">Optional function to generate preview content based on input.</param>
        /// <param name="templateType">Optional template type to show template dropdown.</param>
        public InputDialog(string title, string prompt, string defaultValue = "", Func<string, string> previewGenerator = null, TemplateType? templateType = null)
        {
            _previewGenerator = previewGenerator;
            _templateType = templateType;
            _defaultFileName = defaultValue;
            _userModifiedFileName = false;

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

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt label
            if (templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Template label
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Template combo
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filename label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filename textbox
            if (previewGenerator != null || templateType != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preview label
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Preview textbox
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons row
            grid.Margin = new Thickness(12);

            var currentRow = 0;

            // Main prompt label
            var label = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(label, currentRow++);
            grid.Children.Add(label);

            // Template dropdown (if template type specified)
            if (templateType != null)
            {
                var templateLabel = new TextBlock
                {
                    Text = "Template:",
                    Margin = new Thickness(0, 0, 0, 4)
                };
                templateLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetRow(templateLabel, currentRow++);
                grid.Children.Add(templateLabel);

                _templateComboBox = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    IsEditable = false
                };
                // Use VS themed style for proper dark/light mode support
                _templateComboBox.SetResourceReference(ComboBox.StyleProperty, VsResourceKeys.ComboBoxStyleKey);

                // Add default custom option
                _templateComboBox.Items.Add(_customTemplateText);
                _templateComboBox.SelectedIndex = 0;

                _templateComboBox.SelectionChanged += (s, args) => OnTemplateSelectionChanged();

                Grid.SetRow(_templateComboBox, currentRow++);
                grid.Children.Add(_templateComboBox);
            }

            // Filename label
            var fileNameLabel = new TextBlock
            {
                Text = "File name:",
                Margin = new Thickness(0, 0, 0, 4)
            };
            fileNameLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(fileNameLabel, currentRow++);
            grid.Children.Add(fileNameLabel);

            // Filename textbox
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

            // Add preview section if generator provided or templates enabled
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

                _previewBox = new TextBox
                {
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(4)
                };
                _previewBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
                _previewBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
                _previewBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
                Grid.SetRow(_previewBox, currentRow++);
                grid.Children.Add(_previewBox);
            }

            // Button row container - holds status on left, buttons on right
            var buttonRowGrid = new Grid();
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Left side (status)
            buttonRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Right side (buttons)
            Grid.SetRow(buttonRowGrid, currentRow);

            // Status panel on the left (if templates enabled)
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
                    Content = "\u21BB", // Clockwise open circle arrow (refresh icon)
                    Width = 23,
                    Height = 23,
                    Padding = new Thickness(0),
                    FontSize = 14,
                    ToolTip = "Refresh templates from GitHub",
                    Margin = new Thickness(0, 0, 8, 0)
                };
                _refreshButton.SetResourceReference(Button.StyleProperty, VsResourceKeys.ButtonStyleKey);
                _refreshButton.Click += OnRefreshButtonClick;
                statusPanel.Children.Add(_refreshButton);

                _statusText = new TextBlock
                {
                    Text = "",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11
                };
                _statusText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                statusPanel.Children.Add(_statusText);

                Grid.SetColumn(statusPanel, 0);
                buttonRowGrid.Children.Add(statusPanel);
            }

            // Button panel on the right
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
        }

#pragma warning disable VSTHRD100 // Avoid async void methods - this is an event handler with try-catch
        private async void OnDialogLoaded(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
        {
            _textBox.Focus();
            _textBox.SelectAll();
            UpdatePreview();

            // Load templates asynchronously
            if (_templateType != null && _templateComboBox != null)
            {
                try
                {
                    await LoadTemplatesAsync();
                }
                catch
                {
                    // Handled in LoadTemplatesAsync
                }
            }
        }

        private async Task LoadTemplatesAsync(bool forceRefresh = false)
        {
            try
            {
                SetStatus("Fetching templates from GitHub...");
                SetRefreshEnabled(false);

                if (forceRefresh)
                {
                    AwesomeCopilotService.ClearCache(_templateType.Value);
                }

                _templates = await AwesomeCopilotService.GetTemplatesAsync(_templateType.Value);

                // Update combo box - we're on UI thread after await
                // Remove all items except the first (Custom)
                while (_templateComboBox.Items.Count > 1)
                {
                    _templateComboBox.Items.RemoveAt(1);
                }

                // Add templates
                foreach (TemplateInfo template in _templates)
                {
                    _templateComboBox.Items.Add(template.FileName);
                }

                if (_templates.Count > 0)
                {
                    SetStatus($"Loaded {_templates.Count} templates");
                }
                else
                {
                    SetStatus("No templates found");
                }
            }
            catch
            {
                // Failed to load templates - remove loading indicator if present
                while (_templateComboBox.Items.Count > 1)
                {
                    _templateComboBox.Items.RemoveAt(1);
                }
                SetStatus("Failed to load templates");
            }
            finally
            {
                SetRefreshEnabled(true);
            }
        }

#pragma warning disable VSTHRD100 // Avoid async void methods - this is an event handler with try-catch
        private async void OnRefreshButtonClick(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
        {
            try
            {
                await LoadTemplatesAsync(forceRefresh: true);
            }
            catch
            {
                // Handled in LoadTemplatesAsync
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
            // Track if user manually modified the filename
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

            // Only update preview if using custom template with a preview generator
            if (_templateComboBox == null || _templateComboBox.SelectedIndex == 0)
            {
                UpdatePreview();
            }
        }

#pragma warning disable VSTHRD100 // Avoid async void methods - this is an event handler with try-catch
        private async void OnTemplateSelectionChanged()
#pragma warning restore VSTHRD100
        {
            try
            {
                if (_templateComboBox.SelectedIndex == 0)
                {
                    // Custom template selected
                    SelectedTemplateContent = null;
                    if (!_userModifiedFileName)
                    {
                        _textBox.Text = _defaultFileName;
                    }
                    UpdatePreview();
                }
                else if (_templates != null && _templateComboBox.SelectedIndex > 0)
                {
                    TemplateInfo template = _templates[_templateComboBox.SelectedIndex - 1];

                    // Auto-fill filename unless user has manually edited it
                    if (!_userModifiedFileName)
                    {
                        _textBox.Text = template.FileName;
                    }

                    // Load and show template content
                    if (string.IsNullOrEmpty(template.Content))
                    {
                        SetStatus("Loading template content...");
                        template.Content = await AwesomeCopilotService.GetTemplateContentAsync(template);
                        SetStatus($"Loaded {_templates.Count} templates");
                    }

                    SelectedTemplateContent = template.Content;
                    UpdatePreviewWithContent(template.Content);
                }
            }
            catch
            {
                SetStatus("Failed to load template content");
            }
        }

        private void UpdatePreview()
        {
            if (_previewBox == null)
                return;

            // If a template is selected, don't update based on filename
            if (_templateComboBox != null && _templateComboBox.SelectedIndex > 0)
                return;

            if (_previewGenerator == null)
            {
                _previewBox.Text = string.Empty;
                return;
            }

            try
            {
                var preview = _previewGenerator(_textBox.Text);
                UpdatePreviewWithContent(preview);
            }
            catch
            {
                _previewBox.Text = "(Preview unavailable)";
            }
        }

        private void UpdatePreviewWithContent(string content)
        {
            if (_previewBox == null || content == null)
                return;

            // Limit preview to first ~50 lines for performance
            var lines = content.Split('\n');
            if (lines.Length > 50)
            {
                _previewBox.Text = string.Join("\n", lines, 0, 50) + "\n\n... (truncated)";
            }
            else
            {
                _previewBox.Text = content;
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
