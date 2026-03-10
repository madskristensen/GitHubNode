using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Dialog for adding a new marketplace by owner/repo or GitHub URL.
    /// </summary>
    internal sealed class AddMarketplaceDialog : DialogWindow
    {
        private const int _dwmwaUseImmersiveDarkMode = 20;
        private const int _dwmwaCaptionColor = 35;
        private const int _dwmwaTextColor = 36;

        private readonly TextBox _inputTextBox;

        /// <summary>
        /// Gets the marketplace input entered by the user.
        /// </summary>
        public string MarketplaceInput => _inputTextBox.Text?.Trim();

        public AddMarketplaceDialog()
        {
            Title = "Add Marketplace";
            Width = 450;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
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

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Instructions
            var instructionText = new TextBlock
            {
                Text = "Enter a GitHub repository in one of these formats:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            instructionText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(instructionText, 0);
            grid.Children.Add(instructionText);

            // Examples
            var examplesText = new TextBlock
            {
                Text = "  owner/repo\n  https://github.com/owner/repo",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 12),
                Opacity = 0.8
            };
            examplesText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(examplesText, 1);
            grid.Children.Add(examplesText);

            // Input textbox
            _inputTextBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 16),
                Padding = new Thickness(4, 2, 4, 2)
            };
            _inputTextBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _inputTextBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _inputTextBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            AutomationProperties.SetName(_inputTextBox, "Marketplace repository");
            AutomationProperties.SetHelpText(_inputTextBox, "Enter owner/repo or a GitHub URL");
            Grid.SetRow(_inputTextBox, 2);
            grid.Children.Add(_inputTextBox);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var addButton = CreateThemedButton("Add", isDefault: true);
            addButton.Margin = new Thickness(0, 0, 8, 0);
            addButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_inputTextBox.Text))
                {
                    _ = VS.MessageBox.ShowWarningAsync("Invalid Input", "Please enter a repository in owner/repo format or a GitHub URL.");
                    return;
                }
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(addButton);

            var cancelButton = CreateThemedButton("Cancel", isCancel: true);
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            Content = grid;

            Loaded += (s, e) =>
            {
                _inputTextBox.Focus();
            };

            SourceInitialized += OnSourceInitialized;
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
