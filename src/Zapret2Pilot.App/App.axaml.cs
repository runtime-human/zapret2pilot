using Avalonia.Markup.Xaml;

namespace Zapret2Pilot.App;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // MainWindow is provided by the desktop lifetime callback in Program.Main.
        base.OnFrameworkInitializationCompleted();
    }
}
