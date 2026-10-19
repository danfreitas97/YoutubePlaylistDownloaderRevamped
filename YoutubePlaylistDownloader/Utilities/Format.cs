namespace YoutubePlaylistDownloader.Utilities;

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString(unit == 0 || value >= 100 ? "0" : "0.#")} {Units[unit]}";
    }

    /// <summary>1:05:09, 12:03 or 0:42 — the way YouTube shows durations.</summary>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    /// <summary>"1 h 56 min" or "12 min" for totals.</summary>
    public static string LongDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes} min";

        return duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min" : $"{duration.Seconds} s";
    }
}
