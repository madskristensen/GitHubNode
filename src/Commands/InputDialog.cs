using System.Windows;
using System.Windows.Controls;
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
            Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            HasMaximizeButton = false;
            HasMinimizeButton = false;

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
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            _textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 23,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 23,
                IsCancel = true
            };
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
    }
}
