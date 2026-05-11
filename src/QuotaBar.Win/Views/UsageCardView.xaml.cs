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
}
