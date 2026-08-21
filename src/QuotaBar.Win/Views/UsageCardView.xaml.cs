using System.Windows;
using System.Windows.Controls;
using QuotaBar.Core.Models;

namespace QuotaBar.Win.Views;

public partial class UsageCardView : System.Windows.Controls.UserControl
{
    public UsageCardView()
    {
        InitializeComponent();
    }

    public string PlatformName
    {
        get => (string)GetValue(PlatformNameProperty);
        set => SetValue(PlatformNameProperty, value);
    }

    public static readonly DependencyProperty PlatformNameProperty =
        DependencyProperty.Register(nameof(PlatformName), typeof(string), typeof(UsageCardView), new PropertyMetadata(string.Empty));

    public List<QuotaEntry> Entries
    {
        get => (List<QuotaEntry>)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public static readonly DependencyProperty EntriesProperty =
        DependencyProperty.Register(nameof(Entries), typeof(List<QuotaEntry>), typeof(UsageCardView), new PropertyMetadata(null));

    public double FontScale
    {
        get => (double)GetValue(FontScaleProperty);
        set => SetValue(FontScaleProperty, value);
    }

    public static readonly DependencyProperty FontScaleProperty =
        DependencyProperty.Register(nameof(FontScale), typeof(double), typeof(UsageCardView), new PropertyMetadata(1d));

    public bool ShowGlmKstWarning
    {
        get => (bool)GetValue(ShowGlmKstWarningProperty);
        set => SetValue(ShowGlmKstWarningProperty, value);
    }

    public static readonly DependencyProperty ShowGlmKstWarningProperty =
        DependencyProperty.Register(nameof(ShowGlmKstWarning), typeof(bool), typeof(UsageCardView), new PropertyMetadata(false));

    public string GlmPeakWarningText
    {
        get => (string)GetValue(GlmPeakWarningTextProperty);
        set => SetValue(GlmPeakWarningTextProperty, value);
    }

    public static readonly DependencyProperty GlmPeakWarningTextProperty =
        DependencyProperty.Register(nameof(GlmPeakWarningText), typeof(string), typeof(UsageCardView), new PropertyMetadata(""));
}
