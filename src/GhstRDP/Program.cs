using Velopack;

namespace GhstRDP;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: Velopack hooks install, update and uninstall through it.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
