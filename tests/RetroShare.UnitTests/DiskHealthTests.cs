using RetroShare.Application.Common;
using RetroShare.Domain.Constants;
using Xunit;

namespace RetroShare.UnitTests;

public class DiskHealthTests
{
    [Theory]
    [InlineData(0, DiskHealth.Healthy)]
    [InlineData(45.3, DiskHealth.Healthy)]
    [InlineData(79.9, DiskHealth.Healthy)]
    [InlineData(80, DiskHealth.Warning)]
    [InlineData(85, DiskHealth.Warning)]
    [InlineData(89.9, DiskHealth.Warning)]
    [InlineData(90, DiskHealth.Critical)]
    [InlineData(97.5, DiskHealth.Critical)]
    [InlineData(100, DiskHealth.Critical)]
    public void Classify_DefaultThresholds_MatchBands(double usedPercent, string expected)
        => Assert.Equal(expected, DiskHealth.Classify(usedPercent, 80, 90));

    [Fact]
    public void Classify_BoundaryExactlyAtWarning_IsWarning()
        => Assert.Equal(DiskHealth.Warning, DiskHealth.Classify(80.0, 80, 90));

    [Fact]
    public void Classify_BoundaryExactlyAtCritical_IsCritical()
        => Assert.Equal(DiskHealth.Critical, DiskHealth.Classify(90.0, 80, 90));

    [Fact]
    public void Classify_CustomThresholds_AreHonored()
        => Assert.Equal(DiskHealth.Warning, DiskHealth.Classify(51.0, 50, 99));

    [Fact]
    public void Classify_ReversedThresholds_CriticalWins()
        => Assert.Equal(DiskHealth.Critical, DiskHealth.Classify(85.0, 90, 80));

    [Fact]
    public void SystemMonitorPermission_IsInCatalog()
        => Assert.Contains(Permissions.All, p => p.Name == Permissions.SystemMonitor);

    [Fact]
    public void SystemMonitorPermission_IsGrantedToAdminRole()
        => Assert.Contains(Permissions.AdminRole, name => name == Permissions.SystemMonitor);

    [Fact]
    public void SystemMonitorPermission_IsNotGrantedToBaselineUserRole()
        => Assert.DoesNotContain(Permissions.UserRole, name => name == Permissions.SystemMonitor);
}
