using System;
using System.Threading;
using System.Windows;

namespace DynamicIsland
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            using var m = new Mutex(true, "DynamicIslandWin_single", out bool created);
            if (!created) return;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Run(new MainWindow());
        }
    }
}
