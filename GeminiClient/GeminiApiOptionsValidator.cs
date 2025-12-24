// GeminiClient/GeminiApiOptionsValidator.cs (New file for manual validation)
using Microsoft.Extensions.Options;

namespace GeminiClient;

public class GeminiApiOptionsValidator : IValidateOptions<GeminiApiOptions>
{
    public ValidateOptionsResult Validate(string? name, GeminiApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail("ApiKey is required");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return ValidateOptionsResult.Fail("BaseUrl is required");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail("BaseUrl must be a valid URL");
        }

        if (options.TimeoutSeconds < 1 || options.TimeoutSeconds > 300)
        {
            return ValidateOptionsResult.Fail("TimeoutSeconds must be between 1 and 300");
        }

        if (options.MaxRetries < 0 || options.MaxRetries > 10)
        {
            return ValidateOptionsResult.Fail("MaxRetries must be between 0 and 10");
        }

        return ValidateOptionsResult.Success;
    }
}
