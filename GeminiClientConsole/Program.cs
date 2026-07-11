// GeminiClientConsole/Program.cs
using GeminiClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeminiClientConsole;

public class Program
{
    private const string GeminiConfigSectionName = "GeminiSettings";

    public static async Task<int> Main(string[] args)
    {
        using IHost host = BuildHost(args);

        try
        {
            using IServiceScope scope = host.Services.CreateScope();
            AppRunner runner = scope.ServiceProvider.GetRequiredService<AppRunner>();
            await runner.RunAsync();
            return 0;
        }
        catch (OptionsValidationException ex)
        {
            WriteStartupError("Configuration validation failed:");
            foreach (string failure in ex.Failures)
            {
                Console.Error.WriteLine($"  - {failure}");
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("Set your Gemini API key using any of the following:");
            Console.Error.WriteLine("  • Environment variable: GeminiSettings__ApiKey=your-key");
            Console.Error.WriteLine($"  • Per-user config file : {Path.Combine(ConversationLogger.GetConfigDirectory(), "appsettings.json")}");
            Console.Error.WriteLine("  • dotnet user-secrets (development)");
            return 1;
        }
        catch (Exception ex)
        {
            WriteStartupError($"Application startup failed: {ex.Message}");
            return 2;
        }
    }

    private static IHost BuildHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // A global CLI tool can be launched from any working directory, so resolve config
                // relative to the executable — never the current directory. Precedence (low→high):
                // packaged appsettings.json < per-user config file < environment < user-secrets < args.
                config.Sources.Clear();

                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                    optional: true, reloadOnChange: false);

                string userConfig = Path.Combine(ConversationLogger.GetConfigDirectory(), "appsettings.json");
                config.AddJsonFile(userConfig, optional: true, reloadOnChange: false);

                config.AddEnvironmentVariables();

                if (context.HostingEnvironment.IsDevelopment())
                {
                    config.AddUserSecrets<Program>(optional: true);
                }

                config.AddCommandLine(args);
            })
            .ConfigureLogging((context, logging) =>
            {
                // The console is reserved for the interactive UI. All framework/library logs go to a
                // per-session diagnostics file so nothing interleaves with prompts and responses.
                logging.ClearProviders();
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddProvider(new FileLoggerProvider(minLevel: LogLevel.Trace));
            })
            .ConfigureServices((context, services) =>
            {
                IConfigurationSection geminiConfigSection = context.Configuration.GetSection(GeminiConfigSectionName);
                if (!geminiConfigSection.Exists())
                {
                    WriteStartupError(
                        $"Configuration section '{GeminiConfigSectionName}' not found. " +
                        "Check appsettings.json, the per-user config file, user secrets, or environment variables.");
                }

                _ = services.AddGeminiApiClient(geminiConfigSection);

                _ = services.AddSingleton<ConversationLogger>();
                _ = services.AddTransient<ConsoleModelSelector>();
                _ = services.AddTransient<AppRunner>();
            })
            .Build();

    private static void WriteStartupError(string message)
    {
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
        }
        catch (IOException)
        {
            // no console; ignore
        }

        Console.Error.WriteLine($"ERROR: {message}");

        try
        {
            Console.ResetColor();
        }
        catch (IOException)
        {
            // no console; ignore
        }
    }
}
