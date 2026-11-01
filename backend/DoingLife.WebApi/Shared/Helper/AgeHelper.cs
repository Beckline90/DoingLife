namespace DoingLife.WebApi.Shared.Helper;

public static class AgeHelper
{
    public static int CalculateAge(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;

        // Subtract a year if the birthday hasn't happened yet this year
        if (dateOfBirth > today.AddYears(-age))
            age--;

        return age;
    }
}