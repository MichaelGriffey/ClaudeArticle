using System.Diagnostics.CodeAnalysis;

namespace Ingestion.Domain;

/// <summary>Success or an expected failure, as a value. Callers must handle both.</summary>
public sealed class Result<T, TError>
    where T : notnull
    where TError : notnull
{
    private readonly T? _value;
    private readonly TError? _error;

    private Result(T value) => (_value, IsSuccess) = (value, true);
    private Result(TError error) => _error = error;

    public bool IsSuccess { get; }
    public bool IsError => !IsSuccess;

    public bool TryGetValue([NotNullWhen(true)] out T? value, [NotNullWhen(false)] out TError? error)
    {
        (value, error) = (_value, _error);
        return IsSuccess;
    }

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TError, TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);
        return IsSuccess ? onSuccess(_value!) : onError(_error!);
    }

    public static implicit operator Result<T, TError>(T value) => new(value);
    public static implicit operator Result<T, TError>(TError error) => new(error);
}
