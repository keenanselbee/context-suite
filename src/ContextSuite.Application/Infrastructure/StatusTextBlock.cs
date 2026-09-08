using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ContextSuite.Application.Infrastructure;

// Status text needs an explicit UIA event; LiveSetting alone does not announce updates.
public sealed class StatusTextBlock : TextBlock
{
    private DispatcherOperation? _announcement;

    static StatusTextBlock()
    {
        TextProperty.OverrideMetadata(typeof(StatusTextBlock), new FrameworkPropertyMetadata(OnTextChanged));
    }

    public StatusTextBlock()
    {
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Unloaded += (_, _) =>
        {
            _announcement?.Abort();
            _announcement = null;
        };
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var status = (StatusTextBlock)sender;
        if (!status.IsLoaded || !status.IsVisible || status._announcement is not null ||
            !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;

        // Combine changes made during one dispatcher turn; announce the final text,
        // without moving focus or creating UI for a hidden quick operation.
        status._announcement = status.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            status._announcement = null;
            if (!status.IsLoaded || !status.IsVisible || string.IsNullOrWhiteSpace(status.Text) ||
                AutomationProperties.GetLiveSetting(status) == AutomationLiveSetting.Off) return;
            var peer = UIElementAutomationPeer.FromElement(status) ?? UIElementAutomationPeer.CreatePeerForElement(status);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }
}
