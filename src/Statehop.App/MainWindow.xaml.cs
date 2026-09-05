using Microsoft.UI.Xaml;

namespace Statehop.App;

/// <summary>
/// The viewer window. Statehop keeps running when this closes — closing it
/// hides it back to the tray, and only the tray menu's Exit really stops the
/// app. Letting the close button end observation would silently stop
/// recording the day, which is exactly what Phase 0 has to prove it does not
/// do.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        // Tall enough that the Phase 0 numbers are visible without scrolling on a
        // 1080p display.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 940));

        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            AppWindow.Hide();
        };

        RootFrame.Navigate(typeof(MainPage));
    }
}
