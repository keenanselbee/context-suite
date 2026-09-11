using System.Windows;
using System.Windows.Controls;

namespace ContextSuite.Application;

public partial class ImagePdfOrderWindow : Window
{
    public ImagePdfOrderWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
        Loaded += (_, _) =>
        {
            PageList.UpdateLayout();
            if (PageList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first) first.Focus();
            else PageList.Focus();
        };
        PageList.SelectionChanged += (_, _) =>
        {
            if (PageList.SelectedItem is { } selected) PageList.ScrollIntoView(selected);
        };
    }
    private void OnLicense(object sender, RoutedEventArgs e) { ((App)System.Windows.Application.Current).ShowLicense(); }
    private void OnCancel(object sender, RoutedEventArgs e) { Close(); }
}
