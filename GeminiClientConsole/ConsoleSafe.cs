// GeminiClientConsole/ConsoleSafe.cs
namespace GeminiClientConsole;

/// <summary>
/// Thin wrappers around <see cref="Console"/> that never throw when output is redirected
/// (piped to a file, running under CI, or a terminal that doesn't support colour/size).
/// The raw <see cref="Console"/> APIs throw <see cref="IOException"/> for cursor/width operations
/// when there is no real console, which is exactly the situation where the fancy UI should degrade
/// gracefully rather than crash.
/// </summary>
internal static class ConsoleSafe
{
    private const int FallbackWidth = 80;

    public static bool CanStyle => !Console.IsOutputRedirected;

    public static int Width
    {
        get
        {
            if (Console.IsOutputRedirected)
            {
                return FallbackWidth;
            }

            try
            {
                int width = Console.WindowWidth;
                return width > 0 ? width : FallbackWidth;
            }
            catch (IOException)
            {
                return FallbackWidth;
            }
        }
    }

    public static void SetColor(ConsoleColor color)
    {
        if (!CanStyle)
        {
            return;
        }

        try
        {
            Console.ForegroundColor = color;
        }
        catch (IOException)
        {
            // no console; ignore
        }
    }

    public static void ResetColor()
    {
        if (!CanStyle)
        {
            return;
        }

        try
        {
            Console.ResetColor();
        }
        catch (IOException)
        {
            // no console; ignore
        }
    }

    public static void ClearLine()
    {
        if (!CanStyle)
        {
            return;
        }

        try
        {
            Console.Write('\r' + new string(' ', Math.Max(0, Width - 1)) + '\r');
        }
        catch (IOException)
        {
            // no console; ignore
        }
    }

    public static void WriteColored(string text, ConsoleColor color)
    {
        SetColor(color);
        Console.Write(text);
        ResetColor();
    }

    public static void WriteLineColored(string text, ConsoleColor color)
    {
        SetColor(color);
        Console.WriteLine(text);
        ResetColor();
    }
}
