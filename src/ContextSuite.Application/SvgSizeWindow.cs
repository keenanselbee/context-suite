using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Svg;
using ContextSuite.Core.Images;

namespace ContextSuite.Application;

internal sealed class SvgSizeWindow : Window
{
    private readonly IReadOnlyList<SvgSourceFacts> _sources;
    private readonly ImageFormat _target;
    private readonly ComboBox _background = new() { ItemsSource = new[] { "White", "Black", "Custom RGB" }, SelectedIndex = 0,
        MinWidth = 170, Margin = new(0, 8, 0, 8) };
    private readonly TextBox _rgb = new() { Text = "FFFFFF", MaxLength = 6, IsEnabled = false };
    private readonly ComboBox _choice = new() { ItemsSource = new[] { "Automatic", "Custom width" }, SelectedIndex = 0,
        MinWidth = 170, Margin = new(0, 8, 0, 8) };
    private readonly TextBox _width = new() { Text = "1024", MaxLength = 4, MinWidth = 100 };
    private readonly StatusTextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
    public SvgConversionOptions? Options { get; private set; }

    public SvgSizeWindow(IReadOnlyList<SvgSourceFacts> sources, ImageFormat target = ImageFormat.Png)
    {
        if (sources.Count == 0) throw new InvalidDataException("No SVG sources to review.");
        _sources = sources;
        if (target is not (ImageFormat.Png or ImageFormat.Jpeg)) throw new InvalidDataException("Unsupported SVG output.");
        _target = target;
        Title = "Choose image size — Context Suite";
        Width = 480;
        MinWidth = 360;
        MinHeight = 280;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height * 0.85);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        AutomationProperties.SetAutomationId(this, "SvgSizeWindow");
        AutomationProperties.SetAutomationId(_choice, "SvgSizeChoice");
        AutomationProperties.SetName(_choice, "Image size rule");
        AutomationProperties.SetAutomationId(_width, "SvgCustomWidth");
        AutomationProperties.SetName(_width, "Custom image width in pixels");
        AutomationProperties.SetAutomationId(_error, "SvgSizeStatus");
        _choice.SelectionChanged += (_, _) => _width.IsEnabled = _choice.SelectedIndex == 1;
        _width.IsEnabled = false;
        var grid = new Grid { Margin = new(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock { Text = target == ImageFormat.Jpeg ? "JPEG size and background" : "PNG size", FontSize = 22, FontWeight = FontWeights.SemiBold };
        grid.Children.Add(heading);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 10, 0, 10) };
        AutomationProperties.SetAutomationId(scroll, "SvgSizeBody");
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        var panel = new StackPanel { Margin = new(20) };
        scroll.Content = panel;
        panel.Children.Add(new TextBlock { Text = sources.Count == 1 ? "Choose the image size for this SVG." :
            $"Choose one image size rule for these {sources.Count} SVG files.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Label { Content = "_Size rule", Target = _choice, Padding = new(0), Margin = new(0, 10, 0, 0) });
        panel.Children.Add(_choice);
        panel.Children.Add(new TextBlock { Text = "Automatic keeps each original size when it fits, then reduces it within the rendering limit.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        if (sources.Count == 1)
        {
            var automatic = new SvgConversionOptions().GetSize(sources[0]);
            panel.Children.Add(new TextBlock { Text = $"Automatic for this SVG: {automatic.Width:N0} × {automatic.Height:N0} pixels.",
                TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 0) });
        }
        panel.Children.Add(new Label { Content = "Custom _width (pixels, 1–4096)", Target = _width,
            Padding = new(0), Margin = new(0, 10, 0, 3) });
        panel.Children.Add(_width);
        panel.Children.Add(new TextBlock { Text = "Height follows each SVG's proportions. Large widths may exceed a file's limit.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 5, 0, 0) });
        var notices = sources.SelectMany(source => source.FontNotices.Select(notice => $"{System.IO.Path.GetFileName(source.Path)}: {notice}"))
            .ToArray();
        if (notices.Length > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Font substitutions to review:", FontWeight = FontWeights.SemiBold,
                Margin = new(0, 14, 0, 4) });
            panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = string.Join("\n", notices), TextWrapping = TextWrapping.Wrap },
                MaxHeight = 175, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        if (target == ImageFormat.Jpeg)
        {
            panel.Children.Add(new TextBlock { Text = "JPEG uses quality 90. Choose the color behind transparent areas; the original SVG is kept.",
                TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 4) });
            panel.Children.Add(new Label { Content = "_Background", Target = _background, Padding = new(0) });
            AutomationProperties.SetAutomationId(_background, "SvgJpegBackground");
            AutomationProperties.SetName(_background, "JPEG background color");
            AutomationProperties.SetAutomationId(_rgb, "SvgJpegCustomBackground");
            AutomationProperties.SetName(_rgb, "Custom background RGB, six hexadecimal digits");
            _background.SelectionChanged += (_, _) => _rgb.IsEnabled = _background.SelectedIndex == 2;
            panel.Children.Add(_background);
            panel.Children.Add(new Label { Content = "Custom _RGB (six hex digits)", Target = _rgb, Padding = new(0) });
            panel.Children.Add(_rgb);
        }
        panel.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 10, 0, 0) };
        Grid.SetRow(actions, 2);
        var cancel = new Button { Content = "Cancel", MinWidth = 78, Margin = new(0, 0, 8, 0), IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "CancelSvgSize");
        actions.Children.Add(cancel);
        var confirm = new Button { Content = "Convert", MinWidth = 78, IsDefault = true };
        AutomationProperties.SetAutomationId(confirm, "ConfirmSvgSize");
        confirm.Click += (_, _) => Confirm();
        actions.Children.Add(confirm);
        grid.Children.Add(actions);
        Content = grid;
    }

    private void Confirm()
    {
        _error.Text = "";
        SvgConversionOptions options;
        if (_choice.SelectedIndex == 0) options = new();
        else if (uint.TryParse(_width.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var width))
            options = new(width);
        else { _error.Text = "Enter a whole width from 1 to 4096 pixels."; return; }
        uint? matte = null;
        if (_target == ImageFormat.Jpeg)
        {
            if (_background.SelectedIndex == 0) matte = 0xffffff;
            else if (_background.SelectedIndex == 1) matte = 0;
            else if (_rgb.Text.Length == 6 && uint.TryParse(_rgb.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
                matte = color;
            else { _error.Text = "Enter six hexadecimal digits for the background, such as FFFFFF."; return; }
        }
        options = options with { Target = _target, MatteRgb = matte };
        try
        {
            foreach (var source in _sources) _ = options.GetSize(source);
        }
        catch (InvalidDataException)
        {
            _error.Text = "That width is too large for one or more SVGs. Choose a smaller width.";
            return;
        }
        Options = options;
        DialogResult = true;
    }
}
