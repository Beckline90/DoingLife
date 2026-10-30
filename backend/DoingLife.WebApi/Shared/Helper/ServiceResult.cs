namespace DoingLife.WebApi.Shared.Helper;

public class ServiceResult
{
    public bool Succeeded { get; init; }
    public List<string> Errors { get; init; } = new();

    public static ServiceResult Success() =>
        new() { Succeeded = true };

    public static ServiceResult Failure(string error) =>
        new() { Succeeded = false, Errors = new List<string> { error } };

    public static ServiceResult Failure(IEnumerable<string> errors) =>
        new() { Succeeded = false, Errors = errors.ToList() };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }

    public static ServiceResult<T> Success(T data) =>
        new() { Succeeded = true, Data = data };

    public static new ServiceResult<T> Failure(string error) =>
        new() { Succeeded = false, Errors = new List<string> { error } };

    public static new ServiceResult<T> Failure(IEnumerable<string> errors) =>
        new() { Succeeded = false, Errors = errors.ToList() };
}