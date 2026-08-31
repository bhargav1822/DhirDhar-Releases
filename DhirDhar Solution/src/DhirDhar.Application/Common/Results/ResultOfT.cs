namespace DhirDhar.Application.Common.Results;

/// <summary>
/// Represents the outcome of an operation that produces a value on success.
/// </summary>
/// <typeparam name="TValue">The type of the produced value.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected Result(bool isSuccess, string? error, TValue? value)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value
    {
        get
        {
            if (IsFailure)
            {
                throw new InvalidOperationException($"Cannot access the value of a failed result. Error: {Error}");
            }

            return _value!;
        }
    }

    public static Result<TValue> Success(TValue value)
    {
        return new Result<TValue>(true, null, value);
    }

    public static new Result<TValue> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new Result<TValue>(false, error, default);
    }
}
