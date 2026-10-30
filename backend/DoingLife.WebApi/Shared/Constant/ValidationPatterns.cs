namespace DoingLife.WebApi.Shared.Constant
{
    public static class ValidationPatterns
    {
        // Letters from any language, with single spaces, hyphens or apostrophes
        // between parts. Examples: "Anna", "Mary-Jane", "O'Brien", "Jean Luc".
        public const string PersonName = @"^[\p{L}\p{M}]+([ '\-][\p{L}\p{M}]+)*$";

        public const string Email = @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$";
    }

}

