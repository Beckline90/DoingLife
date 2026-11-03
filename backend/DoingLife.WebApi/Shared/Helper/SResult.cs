namespace DoingLife.WebApi.Shared.Helper;

public class SResult
{
    public bool Succeeded { get; init; }
    public List<string> Errors { get; init; } = new();

    public static SResult Success() =>
        new() { Succeeded = true };

    public static SResult Failure(string error) =>
        new() { Succeeded = false, Errors = new List<string> { error } };

    public static SResult Failure(IEnumerable<string> errors) =>
        new() { Succeeded = false, Errors = errors.ToList() };
}

public class SResult<T> : SResult
{
    public T? Data { get; init; }

    public static SResult<T> Success(T data) =>
        new() { Succeeded = true, Data = data };

    public static new SResult<T> Failure(string error) =>
        new() { Succeeded = false, Errors = new List<string> { error } };

    public static new SResult<T> Failure(IEnumerable<string> errors) =>
        new() { Succeeded = false, Errors = errors.ToList() };
}