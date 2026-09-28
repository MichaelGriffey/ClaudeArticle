using Ingestion.Domain;

namespace Ingestion.Api.Infrastructure;

/// <summary>Composition-root helpers: configuration the service cannot run with stops startup.</summary>
public static class StartupGuard
{
    /// <summary>
    /// The value, or an exception that stops startup and names what was invalid. For configuration
    /// only; a request path handles both outcomes of a <see cref="Result{T, TError}"/> instead.
    /// </summary>
    public static T OrThrowAtStartup<T, TError>(this Result<T, TError> result, string what)
        where T : notnull
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(value => value, error => throw new InvalidOperationException($"Invalid {what}: {error}"));
    }
}
