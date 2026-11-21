namespace DoingLife.WebApi.Shared.Constant
{
    public static class AppRules
    {
        public const int MinAge = 18;
        public const int MaxAge = 90;

        public static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase) { "en", "fr" };
    }
}
