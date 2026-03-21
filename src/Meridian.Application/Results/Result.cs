namespace Meridian.Application.ResultTypes;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// Provides a functional approach to error handling without exceptions.
/// </summary>
/// <typeparam name="TValue">The type of the success value</typeparam>
/// <typeparam name="TError">The type of the error</typeparam>
public readonly struct Result<TValue, TError>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed Result. Check IsSuccess first.");

    public TError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on a successful Result. Check IsFailure first.");

    private Result(TValue value)
    {
        IsSuccess = true;
        _value = value;
        _error = default;
    }

    private Result(TError error, bool _)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
    }

    public static Result<TValue, TError> Success(TValue value) => new(value);
    public static Result<TValue, TError> Failure(TError error) => new(error, false);

    public static implicit operator Result<TValue, TError>(TValue value) => Success(value);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    public void Match(Action<TValue> onSuccess, Action<TError> onFailure)
    {
        if (IsSuccess)
            onSuccess(_value!);
        else
            onFailure(_error!);
    }

    public Result<TNew, TError> Map<TNew>(Func<TValue, TNew> mapper)
        => IsSuccess
            ? Result<TNew, TError>.Success(mapper(_value!))
            : Result<TNew, TError>.Failure(_error!);

    public Result<TNew, TError> Bind<TNew>(Func<TValue, Result<TNew, TError>> binder)
        => IsSuccess ? binder(_value!) : Result<TNew, TError>.Failure(_error!);

    public Result<TValue, TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
        => IsSuccess
            ? Result<TValue, TNewError>.Success(_value!)
            : Result<TValue, TNewError>.Failure(mapper(_error!));

    public TValue GetValueOrDefault(TValue defaultValue = default!)
        => IsSuccess ? _value! : defaultValue;

    public TValue GetValueOrThrow()
        => IsSuccess ? _value! : throw new InvalidOperationException($"Result failed with error: {_error}");

    public override string ToString()
        => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
}

/// <summary>
/// Non-generic Result for operations that don't return a value
/// </summary>
public readonly struct Result<TError>
{
    private readonly TError? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public TError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on a successful Result. Check IsFailure first.");

    private Result(bool success, TError? error = default)
    {
        IsSuccess = success;
        _error = error;
    }

    public static Result<TError> Success() => new(true);
    public static Result<TError> Failure(TError error) => new(false, error);

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<TError, TResult> onFailure)
        => IsSuccess ? onSuccess() : onFailure(_error!);

    public void Match(Action onSuccess, Action<TError> onFailure)
    {
        if (IsSuccess)
            onSuccess();
        else
            onFailure(_error!);
    }

    public override string ToString()
        => IsSuccess ? "Success" : $"Failure({_error})";
}

/// <summary>
/// Static factory methods for Result types
/// </summary>
public static class Result
{
    public static Result<TValue, TError> Success<TValue, TError>(TValue value)
        => Result<TValue, TError>.Success(value);

    public static Result<TValue, TError> Failure<TValue, TError>(TError error)
        => Result<TValue, TError>.Failure(error);

    public static Result<TError> Success<TError>()
        => Result<TError>.Success();

    public static Result<TError> Failure<TError>(TError error)
        => Result<TError>.Failure(error);

    /// <summary>
    /// Executes a function and captures any exception as a Result
    /// </summary>
    public static Result<TValue, Exception> Try<TValue>(Func<TValue> func)
    {
        try
        {
            return Result<TValue, Exception>.Success(func());
        }
        catch (Exception ex)
        {
            return Result<TValue, Exception>.Failure(ex);
        }
    }

    /// <summary>
    /// Executes an async function and captures any exception as a Result
    /// </summary>
    public static async Task<Result<TValue, Exception>> TryAsync<TValue>(
        Func<Task<TValue>> func,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return Result<TValue, Exception>.Success(await func());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<TValue, Exception>.Failure(ex);
        }
    }

    /// <summary>
    /// Combines multiple results into a single result containing all values
    /// </summary>
    public static Result<IReadOnlyList<TValue>, TError> Combine<TValue, TError>(
        IEnumerable<Result<TValue, TError>> results)
    {
        var values = new List<TValue>();
        foreach (var result in results)
        {
            if (result.IsFailure)
                return Result<IReadOnlyList<TValue>, TError>.Failure(result.Error);
            values.Add(result.Value);
        }
        return Result<IReadOnlyList<TValue>, TError>.Success(values.AsReadOnly());
    }
}
