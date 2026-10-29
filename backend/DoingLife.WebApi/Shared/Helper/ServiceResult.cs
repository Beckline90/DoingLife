namespace DoingLife.WebApi.ViewModel;

public class ServiceResult
{
    public bool Succeeded { get; init; }
    public List<string> Errors { get; init; } = new();

    public static ServiceResult Success() => new() { Succeeded = true };

    public static ServiceResult Failure(IEnumerable<string> errors) =>
        new() { Succeeded = false, Errors = errors.ToList() };
}