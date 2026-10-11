using System;
using System.IO;
using System.Windows;

namespace ViewCraft
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow();
            mainWindow.Show();

            if (e.Args.Length > 0)
            {
                string targetPath = e.Args[0];
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    mainWindow.LoadFromCommandLine(targetPath);
                }
            }
        }
    }
}
