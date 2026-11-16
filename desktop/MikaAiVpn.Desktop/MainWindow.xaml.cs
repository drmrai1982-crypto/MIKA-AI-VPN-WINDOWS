using System.IO;
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

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsButton.IsEnabled = false;
        DiagnosticsText.Text = _isPersian
            ? "در حال بارگذاری هسته و بررسی موتورهای موجود..."
            : "Loading the core and inspecting available engines...";

        try
        {
            var ffiDirectory = Path.Combine(AppContext.BaseDirectory, "hev-engine");
            if (!File.Exists(Path.Combine(ffiDirectory, "fcae_ffi.dll")))
            {
                DiagnosticsText.Text = _isPersian
                    ? "فایل‌های هسته در کنار برنامه پیدا نشدند. بستهٔ کامل ویندوز را اجرا کنید."
                    : "Native core files were not found beside the application. Use the complete Windows package.";
                return;
            }

            var result = await Task.Run(() => NativeBridge.Inspect(ffiDirectory));
            DiagnosticsText.Text = result.Message;

            if (result.Result != 0)
            {
                ConnectionState.Text = _isPersian
                    ? $"بررسی هسته ناموفق بود ({result.Result})"
                    : $"Core inspection failed ({result.Result})";
            }
            else
            {
                ConnectionState.Text = _isPersian
                    ? "هسته بررسی شد؛ تونل شروع نشده است"
                    : "Core inspected; tunnel was not started";
            }
        }
        catch (DllNotFoundException ex)
        {
            DiagnosticsText.Text = (_isPersian
                ? "پل بومی پیدا نشد: "
                : "Native bridge was not found: ") + ex.Message;
        }
        catch (Exception ex)
        {
            DiagnosticsText.Text = (_isPersian ? "خطای بررسی: " : "Inspection error: ") + ex.Message;
        }
        finally
        {
            DiagnosticsButton.IsEnabled = true;
        }
    }

    private void SetLanguage(bool persian)
    {
        FlowDirection = persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LanguageButton.Content = persian ? "English" : "فارسی";
        DiagnosticsButton.Content = persian ? "بررسی هسته و موتورها" : "Inspect core and engines";
        Title = persian ? "مایکا ای‌آی وی‌پی‌ان" : "MIKA AI VPN";
        ConnectionTitle.Text = persian ? "اتصال امن" : "Secure connection";
        ConnectionState.Text = persian ? "هستهٔ VPN ویندوز متصل نیست" : "VPN core not connected";
        ConnectionHint.Text = persian
            ? "موتور تونل بومی ویندوز هنوز به رابط اتصال واقعی متصل نشده است."
            : "The native Windows tunnel is not yet connected to the live connection flow.";
        if (DiagnosticsText.Text.Length == 0 ||
            DiagnosticsText.Text.StartsWith("Native FFI:", StringComparison.Ordinal) ||
            DiagnosticsText.Text.StartsWith("رابط FFI", StringComparison.Ordinal))
        {
            DiagnosticsText.Text = persian
                ? "بررسی هنوز اجرا نشده است. این آزمایش تونل را روشن نمی‌کند."
                : "Inspection has not been run. This test does not start a tunnel.";
        }
    }
}
