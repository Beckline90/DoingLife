namespace DoingLife.WebApi.Shared.Helper;

public static class AgeHelper
{
    public static int CalculateAge(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;

     
        if (dateOfBirth > today.AddYears(-age))
            age--;

        return age;
    }
}