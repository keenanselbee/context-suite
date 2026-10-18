using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;

namespace ContextSuite.Application;

internal sealed class VideoAudioTrackWindow : Window
{
    public VideoAudioSelection? Selection { get; private set; }

    internal VideoAudioTrackWindow(VideoAudioTrackViewModel decision)
    {
        Title = "Choose audio track - Context Suite";
        Width = 600; MinWidth = 380;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height * 0.85);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "VideoAudioTrackWindow");
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(new TextBlock { Text = "Choose audio track", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = decision.FileName, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        var choice = new ComboBox { ItemsSource = decision.Tracks, MinHeight = 32 };
        var trackText = new FrameworkElementFactory(typeof(TextBlock));
        trackText.SetBinding(TextBlock.TextProperty, new Binding(nameof(VideoAudioTrackOption.Label)));
        trackText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        trackText.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(ActualWidth))
        {
            Source = choice, Converter = new TrackWidthConverter()
        });
        choice.ItemTemplate = new DataTemplate { VisualTree = trackText };
        AutomationProperties.SetName(choice, "Audio track");
        AutomationProperties.SetAutomationId(choice, "VideoAudioTrackChoice");
        panel.Children.Add(new Label { Content = "_Audio track", Target = choice, Padding = new(0), Margin = new(0, 0, 0, 6) });
        panel.Children.Add(choice);
        panel.Children.Add(new TextBlock { Text = "Only the selected audio is extracted. The original video is kept.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 6) });
        var status = new StatusTextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new(0, 0, 8, 0) };
        var next = new Button { Content = "_Continue", IsDefault = true, MinWidth = 90, IsEnabled = false };
        AutomationProperties.SetAutomationId(next, "VideoAudioTrackContinue");
        choice.SelectionChanged += (_, _) =>
        {
            var selected = choice.SelectedItem as VideoAudioTrackOption;
            next.IsEnabled = selected is { BlockReason: null };
            status.Text = selected?.BlockReason ?? "The next step checks any quality changes needed for your output format.";
        };
        next.Click += (_, _) =>
        {
            if (choice.SelectedItem is not VideoAudioTrackOption { BlockReason: null } selected) return;
            Selection = selected.Selection;
            DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(next); panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        choice.SelectedItem = decision.Tracks.FirstOrDefault(track => track.BlockReason is null) ?? decision.Tracks.FirstOrDefault();
        Loaded += (_, _) => choice.Focus();
    }

    private sealed class TrackWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Math.Max(120, (double)value - 56);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
