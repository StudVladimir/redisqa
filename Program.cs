using Avalonia;
using System;
using System.IO;

namespace redisqa;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        LoadEnvironmentVariables();
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    
    private static void LoadEnvironmentVariables()
    {
        try
        {
            var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (File.Exists(envPath))
            {
                DotNetEnv.Env.Load(envPath);
                System.Diagnostics.Debug.WriteLine($".env file loaded from: {envPath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($".env file not found at: {envPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading .env file: {ex.Message}");
        }
    }
}
