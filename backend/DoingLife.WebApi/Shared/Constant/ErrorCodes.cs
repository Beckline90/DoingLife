namespace DoingLife.WebApi.Shared.Constant;

public static class ErrorCodes
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string EmailNotVerified = "email_not_verified";
    public const string AccountUnavailable = "account_unavailable";
    public const string InvalidSession = "invalid_session";
    public const string InvalidToken = "invalid_token";
    public const string EmailAlreadyRegistered = "email_already_registered";
    public const string InvalidDateOfBirth = "invalid_date_of_birth";
    public const string UnderAge = "under_age";
    public const string TermsNotAccepted = "terms_not_accepted";
    public const string WeakPassword = "weak_password";
}