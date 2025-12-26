using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A simple input dialog for prompting the user for text input.
    /// Uses Visual Studio theming for consistent appearance.
    /// </summary>
    internal sealed class InputDialog : DialogWindow
    {
        private readonly TextBox _textBox;

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
        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title = title;
            Width = 400;
            SizeToContent = SizeToContent.Height;
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
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(12);

            var label = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(label, 0);
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
            Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var okButton = CreateThemedButton("OK", isDefault: true);
            okButton.Margin = new Thickness(0, 0, 8, 0);
            okButton.Click += (s, e) =>
            {
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = CreateThemedButton("Cancel", isCancel: true);
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
            };
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
