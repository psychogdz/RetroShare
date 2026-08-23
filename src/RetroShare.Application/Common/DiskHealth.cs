namespace RetroShare.Application.Common;

/// <summary>Disk usage health classification for monitoring. Thresholds come from
/// <see cref="StorageOptions"/> and are evaluated against the used percentage.</summary>
public static class DiskHealth
{
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string Critical = "Critical";

    /// <summary>Classifies a used-percentage into a state. Values below the warning threshold
    /// are healthy; the critical threshold wins over warning when misconfigured in reverse.</summary>
    public static string Classify(double usedPercent, int warningThresholdPercent, int criticalThresholdPercent)
    {
        if (usedPercent >= criticalThresholdPercent)
        {
            return Critical;
        }

        if (usedPercent >= warningThresholdPercent)
        {
            return Warning;
        }

        return Healthy;
    }
}
