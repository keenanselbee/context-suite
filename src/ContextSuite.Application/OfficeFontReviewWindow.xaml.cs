using System.IO;
using System.Windows;
using ContextSuite.Core.Office;

namespace ContextSuite.Application;

public partial class OfficeFontReviewWindow : Window
{
    internal bool Accepted { get; private set; }
    public OfficeFontReviewWindow(OfficeFontReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        OfficeHostProtocol.ValidateFontFamilies(review.MissingFontFamilies);
        if (review.MissingFontFamilies.IsEmpty) throw new ArgumentException("Only substituted fonts need review.", nameof(review));
        InitializeComponent();
        SourceName.Text = Path.GetFileName(review.SourcePath);
        FontNames.Text = string.Join(Environment.NewLine, review.MissingFontFamilies);
        _ = new Infrastructure.ContentWindowSizing(this);
        Loaded += (_, _) => SkipFile.Focus();
    }
    private void OnSave(object sender, RoutedEventArgs e) { Accepted = true; Close(); }
    private void OnSkip(object sender, RoutedEventArgs e) { Close(); }
}
