// GeminiClientConsole/Program.cs
using GeminiClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GeminiClientConsole;

public class Program
{
    private const string GeminiConfigSectionName = "GeminiSettings";

    public static async Task Main(string[] args)
    {
        IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
            })
            .ConfigureServices((context, services) =>
            {
                IConfigurationSection geminiConfigSection = context.Configuration.GetSection(GeminiConfigSectionName);

                if (!geminiConfigSection.Exists())
                {
                    Console.Error.WriteLine($"Configuration section '{GeminiConfigSectionName}' not found. Please check appsettings.json, user secrets, or environment variables.");
                }

                // Register library services (includes IModelService)
                _ = services.AddGeminiApiClient(geminiConfigSection);

                // Register console-specific services
                _ = services.AddSingleton<ConversationLogger>();
                _ = services.AddTransient<ConsoleModelSelector>();
                _ = services.AddTransient<AppRunner>();
            })
            .Build();

        try
        {
            using var scope = host.Services.CreateScope();
            AppRunner runner = scope.ServiceProvider.GetRequiredService<AppRunner>();
            await runner.RunAsync();
        }
        catch (OptionsValidationException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"ERROR: Configuration validation failed.");
            foreach (string failure in ex.Failures)
            {
                Console.Error.WriteLine($"- {failure}");
            }
            Console.ResetColor();
            Console.WriteLine($"Please check your configuration and ensure required values are set.");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"ERROR: Application startup failed. {ex.Message}");
            Console.ResetColor();
            Environment.Exit(2);
        }
    }
}
