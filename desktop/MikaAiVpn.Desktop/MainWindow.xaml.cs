using System.Windows;

namespace MikaAiVpn.Desktop;

public partial class MainWindow : Window
{
    private bool _isPersian;

    public MainWindow()
    {
        InitializeComponent();
        SetLanguage(false);
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        _isPersian = !_isPersian;
        SetLanguage(_isPersian);
    }

    private void SetLanguage(bool persian)
    {
        FlowDirection = persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LanguageButton.Content = persian ? "English" : "فارسی";
        Title = persian ? "مایکا ای‌آی وی‌پی‌ان" : "MIKA AI VPN";
        ConnectionTitle.Text = persian ? "اتصال امن" : "Secure connection";
        ConnectionState.Text = persian ? "هستهٔ VPN ویندوز متصل نیست" : "VPN core not connected";
        ConnectionHint.Text = persian
            ? "موتور تونل بومی ویندوز هنوز به رابط برنامه متصل نشده است."
            : "The native Windows tunnel engine is not yet integrated.";
        DiagnosticsText.Text = persian
            ? "رابط FFI بومی: بارگذاری نشده\nWintun: در این نشست تأیید نشده\nتونل: در دسترس نیست"
            : "Native FFI: not loaded\nWintun: not verified in this desktop session\nTunnel: unavailable";
    }
}
