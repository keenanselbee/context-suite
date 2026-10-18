using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Core.Images;

namespace ContextSuite.Application;

internal sealed class TiffMatteWindow : Window
{
    private readonly ComboBox _choice = new() { ItemsSource = new[] { "White", "Black", "Custom RGB" }, SelectedIndex = 0,
        MinWidth = 160, Margin = new(0, 8, 0, 8) };
    private readonly TextBox _custom = new() { Text = "FFFFFF", MaxLength = 7, MinWidth = 120 };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, Margin = new(0, 5, 0, 0) };
    public uint? MatteRgb { get; private set; }

    public TiffMatteWindow(string name, ImagePreview preview)
    {
        Title = "Choose JPEG background — Context Suite";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        if (preview.Width is 0 or > 512 || preview.Height is 0 or > 512 ||
            preview.BgraPixels is null || preview.BgraPixels.Length != (long)preview.Width * preview.Height * 4)
            throw new InvalidDataException("Invalid TIFF background preview.");
        var bitmap = BitmapSource.Create((int)preview.Width, (int)preview.Height, 96, 96, PixelFormats.Bgra32, null,
            preview.BgraPixels, (int)preview.Width * 4);
        bitmap.Freeze();
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(new TextBlock { Text = $"{name} contains transparent pages. Choose the background used for every JPEG output in this batch.",
            TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Image { Source = bitmap, MaxWidth = 340, MaxHeight = 220, Stretch = Stretch.Uniform,
            Margin = new(0, 12, 0, 4) });
        panel.Children.Add(new TextBlock { Text = "Preview of the first transparent page on white", FontSize = 12 });
        panel.Children.Add(_choice);
        panel.Children.Add(new TextBlock { Text = "Custom background (six hex digits):" });
        panel.Children.Add(_custom);
        panel.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 78, Margin = new(0, 0, 8, 0), IsCancel = true };
        var confirm = new Button { Content = "Convert", MinWidth = 78, IsDefault = true };
        confirm.Click += (_, _) => Confirm();
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        panel.Children.Add(actions);
        Content = panel;
    }

    private void Confirm()
    {
        if (_choice.SelectedIndex == 0) MatteRgb = 0xffffff;
        else if (_choice.SelectedIndex == 1) MatteRgb = 0;
        else
        {
            var text = _custom.Text.Trim().TrimStart('#');
            if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            {
                _error.Text = "Enter six hexadecimal digits, such as FFFFFF.";
                return;
            }
            MatteRgb = rgb;
        }
        DialogResult = true;
    }
}
