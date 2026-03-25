using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LilAgents.Views;

/// <summary>
/// First-run welcome display shown when the user has not completed onboarding.
/// Ported from the macOS openOnboardingPopover flow.
/// </summary>
public sealed partial class OnboardingView : UserControl
{
    /// <summary>
    /// Raised when the user dismisses the onboarding view.
    /// </summary>
    public event Action? Dismissed;

    public OnboardingView()
    {
        InitializeComponent();
    }

    private void OnDismissClicked(object sender, RoutedEventArgs e)
    {
        Dismissed?.Invoke();
    }
}
