using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application.TestHost;

internal static class ImagePdfOrderViewContracts
{
    public static int Run(Window window)
    {
        var passed = 0;
        var root = Path.Combine(Path.GetTempPath(), "unused-image-pdf-order-view");
        var source = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "second folder", "same.png"), new('A', 64), 100,
            ImageFormat.Png, 4, 3, 8, 1, false, false, "sRGB", []);
        var other = source with { ItemId = Guid.NewGuid(), Path = Path.Combine(root, "first folder", "same.png") };
        var access = new SyntheticAccess();
        using var model = new ImagePdfOrderViewModel(ImagePdfPlan.Create(Guid.NewGuid(), [source, other], new("convert", new())), access);
        window.DataContext = model;
        var content = (FrameworkElement)window.Content;
        Layout(window.Width - 16, double.PositiveInfinity);
        var list = Find<ListBox>(window, "ImagePdfPages");
        Check(list.Items.Count == 2 && list.SelectedItem == model.Pages[0] && list.SelectionMode == SelectionMode.Single,
            "request order and selected first page bind to one list");
        var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        Check(first is not null && AutomationProperties.GetName(first).Contains("Page 1: same.png") &&
            AutomationProperties.GetName(first).Contains("second folder"), "list item exposes number, filename and distinct folder");
        var up = Find<Button>(window, "ImagePdfMoveUp"); var down = Find<Button>(window, "ImagePdfMoveDown");
        Check(!up.IsEnabled && down.IsEnabled && !Find<Button>(window, "ConfirmImagePdf").IsEnabled,
            "movement boundaries and license wait drive real button state");
        var shortcuts = window.InputBindings.OfType<KeyBinding>().ToArray();
        Check(shortcuts.Any(key => key.Key == Key.Up && key.Modifiers == ModifierKeys.Alt && key.Command == model.MoveUpCommand) &&
            shortcuts.Any(key => key.Key == Key.Down && key.Modifiers == ModifierKeys.Alt && key.Command == model.MoveDownCommand) &&
            Find<Button>(window, "CancelImagePdf").IsCancel && !Find<Button>(window, "ConfirmImagePdf").IsDefault,
            "keyboard move bindings and Escape are explicit; Enter does not implicitly confirm");
        model.MoveDownCommand.Execute(null); Layout(window.Width - 16, double.PositiveInfinity);
        Check(list.SelectedItem == model.Pages[1] && model.SelectedPage!.Page.Source == source &&
            Find<TextBlock>(window, "ImagePdfDestination").Text.Contains("first folder"),
            "WPF move retains selected image and updates first-page destination");
        Check(model.RefreshAccessAsync().IsCompletedSuccessfully && !Find<Button>(window, "ConfirmImagePdf").IsEnabled &&
            ((string)Find<Button>(window, "ImagePdfOrderLicense").Content).Contains("Activate"),
            "expired access binds an activation action and disabled Convert button");
        access.Allowed = true;
        Check(model.RefreshAccessAsync().IsCompletedSuccessfully && Find<Button>(window, "ConfirmImagePdf").IsEnabled && list.SelectedItem == model.Pages[1],
            "activation refresh enables the actual Convert button without resetting order or selection");
        access.Allowed = false;
        Check(model.RefreshAccessAsync().IsCompletedSuccessfully && !Find<Button>(window, "ConfirmImagePdf").IsEnabled && model.Pages[0].Page.Source == other,
            "deactivation refresh disables the actual Convert button and keeps edited page one");
        var shortHeight = content.DesiredSize.Height;
        var many = Enumerable.Range(0, ImagePdfPlan.MaximumPages).Select(index => source with
        { ItemId = Guid.NewGuid(), Path = Path.Combine(root, "folder " + index, new string('x', 120) + ".png") });
        using var large = new ImagePdfOrderViewModel(ImagePdfPlan.Create(Guid.NewGuid(), many, new("convert", new())), access);
        window.DataContext = large; Layout(window.Width - 16, double.PositiveInfinity);
        Check(list.Items.Count == 4096 && list.ActualHeight <= 281 && content.DesiredSize.Height < shortHeight + 500 &&
            list.ItemContainerGenerator.ContainerFromIndex(4095) is null && VirtualizingPanel.GetIsVirtualizing(list),
            "4096 long filenames stay in a bounded virtualized list");
        large.SelectedPage = large.Pages[^1]; list.ScrollIntoView(large.SelectedPage); Layout(window.Width - 16, double.PositiveInfinity);
        Check(list.SelectedItem == large.Pages[^1] && !Find<Button>(window, "ImagePdfMoveDown").IsEnabled,
            "last page remains reachable without enabling an out-of-range move");
        Layout(window.MinWidth - 16, window.MinHeight - 40);
        var body = Find<ScrollViewer>(window, "ImagePdfOrderBody");
        var status = Find<StatusTextBlock>(window, "ImagePdfOrderStatus");
        var confirm = Find<Button>(window, "ConfirmImagePdf");
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        Check(body.ScrollableHeight > 0 && Bounds(status).Top >= Bounds(body).Bottom && Bounds(confirm).Bottom <= content.RenderSize.Height,
            "minimum size scrolls content while status and Convert remain within the window");
        return passed;

        void Layout(double width, double height)
        {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, double.IsPositiveInfinity(height) ? content.DesiredSize.Height : height));
            content.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("PDF order view: " + name);
            passed++; Console.WriteLine("PASS: PDF order view: " + name);
        }
    }

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T match && AutomationProperties.GetAutomationId(root) == id) return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            try { return Find<T>(child, id); }
            catch (KeyNotFoundException) { }
        }
        throw new KeyNotFoundException(id);
    }
    private sealed class SyntheticAccess : IOperationAccess
    {
        public bool Allowed { get; set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "Active" : "Activate your license to convert."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch plan, CancellationToken token) => throw new InvalidOperationException("Unexpected admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization plan, CancellationToken token) => throw new InvalidOperationException("Unexpected admission.");
    }
}
