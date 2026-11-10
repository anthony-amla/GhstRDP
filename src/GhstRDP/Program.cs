using Velopack;

namespace GhstRDP;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Precisa ser a primeira coisa: o Velopack usa isso na instalação, atualização e desinstalação.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
