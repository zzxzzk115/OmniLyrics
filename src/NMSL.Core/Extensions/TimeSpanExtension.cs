namespace NMSL.Core.Extensions;

public static class TimeSpanExtensions
{
    /// <summary>
    /// Format TimeSpan as mm:ss or hh:mm:ss depending on length.
    /// </summary>
    public static string ToHumanTime(this TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return t.ToString(@"hh\:mm\:ss");
        else
            return t.ToString(@"mm\:ss");
    }
}