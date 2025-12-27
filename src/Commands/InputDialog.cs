using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A simple input dialog for prompting the user for text input.
    /// Uses Visual Studio theming for consistent appearance.
    /// </summary>
    internal sealed class InputDialog : DialogWindow
    {
        private readonly TextBox _textBox;
        private readonly TextBox _previewBox;
        private readonly Func<string, string> _previewGenerator;

        /// <summary>
        /// Gets the text entered by the user.
        /// </summary>
        public string InputText => _textBox.Text;

        /// <summary>
        /// Creates a new input dialog.
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="defaultValue">The default value in the text box.</param>
        /// <param name="previewGenerator">Optional function to generate preview content based on input.</param>
        public InputDialog(string title, string prompt, string defaultValue = "", Func<string, string> previewGenerator = null)
        {
            _previewGenerator = previewGenerator;

            Title = title;
            Width = 500;
            Height = previewGenerator != null ? 400 : double.NaN;
            SizeToContent = previewGenerator != null ? SizeToContent.Manual : SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            HasMaximizeButton = false;
            HasMinimizeButton = false;
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
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (previewGenerator != null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(12);

            int currentRow = 0;

            var label = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(label, currentRow++);
            grid.Children.Add(label);

            _textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(4, 2, 4, 2)
            };
            _textBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _textBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _textBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            Grid.SetRow(_textBox, currentRow++);
            grid.Children.Add(_textBox);

            // Add preview section if generator provided
            if (previewGenerator != null)
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
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
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

                // Update preview when text changes
                _textBox.TextChanged += (s, e) => UpdatePreview();
            }

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, currentRow);

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

            grid.Children.Add(buttonPanel);
            Content = grid;

            Loaded += (s, e) =>
            {
                _textBox.Focus();
                _textBox.SelectAll();
                UpdatePreview();
            };
        }

        private void UpdatePreview()
        {
            if (_previewBox == null || _previewGenerator == null)
                return;

            try
            {
                var preview = _previewGenerator(_textBox.Text);
                // Limit preview to first ~50 lines for performance
                var lines = preview.Split('\n');
                if (lines.Length > 50)
                {
                    _previewBox.Text = string.Join("\n", lines, 0, 50) + "\n\n... (truncated)";
                }
                else
                {
                    _previewBox.Text = preview;
                }
            }
            catch
            {
                _previewBox.Text = "(Preview unavailable)";
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
            button.SetResourceReference(Button.BackgroundProperty, EnvironmentColors.SystemButtonFaceBrushKey);
            button.SetResourceReference(Button.ForegroundProperty, EnvironmentColors.SystemButtonTextBrushKey);
            button.SetResourceReference(Button.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            return button;
        }
    }
}
