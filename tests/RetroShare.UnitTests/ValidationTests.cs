using RetroShare.Application.Common;
using RetroShare.Application.Validation;
using RetroShare.Domain.Entities;
using Xunit;

namespace RetroShare.UnitTests;

public class ValidatorPasswordTests
{
    [Theory]
    [InlineData("Abcdef12", true)]      // lower + upper + digit = 3 classes
    [InlineData("abcdefg1!", true)]     // lower + digit + symbol = 3 classes
    [InlineData("ABCDEFG1", false)]     // upper + digit = only 2 classes
    [InlineData("12345678", false)]     // one class
    [InlineData("abcdefgh", false)]     // one class
    [InlineData("Ab1", false)]          // too short
    [InlineData("", false)]
    public void IsStrongPassword_EnforcesPolicy(string password, bool expected)
    {
        Assert.Equal(expected, Validators.IsStrongPassword(password, out _));
    }

    [Fact]
    public void IsStrongPassword_Rejects_OverlyLong()
    {
        Assert.False(Validators.IsStrongPassword(new string('a', 129) + "A1!", out _));
    }
}

public class ValidatorUsernameTests
{
    [Theory]
    [InlineData("alice", true)]
    [InlineData("user_name-01", true)]
    [InlineData("ab", false)]
    [InlineData("has space", false)]
    [InlineData("hacker/../../etc", false)]
    public void IsValidUsername_ChecksShape(string username, bool expected) =>
        Assert.Equal(expected, Validators.IsValidUsername(username, out _));
}

public class ValidatorEmailTests
{
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("@missing-local.com", false)]
    public void IsValidEmail_ChecksShape(string email, bool expected) =>
        Assert.Equal(expected, Validators.IsValidEmail(email, out _));
}

public class NameSanitizationTests
{
    [Fact]
    public void Sanitizes_PathSeparators_And_Control_Characters()
    {
        var result = Validators.SanitizeName("..\\..\\evil\x01 name.txt");

        Assert.NotNull(result);
        Assert.Equal("evil name.txt", result);
        Assert.All(result!, c => Assert.False(char.IsControl(c), $"control char U+{(int)c:X4} leaked"));
        Assert.DoesNotContain('\\', result);
        Assert.DoesNotContain('/', result);
    }

    [Fact]
    public void Collapses_Whitespace_And_Trims()
    {
        Assert.Equal("my file.txt", Validators.SanitizeName("  my    file.txt  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void Rejects_EmptyNames(string? raw) =>
        Assert.Null(Validators.SanitizeName(raw!));

    [Fact]
    public void Truncates_ToMaxLength()
    {
        var longName = new string('a', 500) + ".txt";
        Assert.True(Validators.SanitizeName(longName)!.Length <= Domain.Constants.FileRules.MaxNameLength);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("com1.txt")]
    [InlineData("NUL.log")]
    public void Detects_ReservedNames(string name) =>
        Assert.True(Validators.IsReservedName(name));

    [Theory]
    [InlineData("console.txt")]
    [InlineData("normal.pdf")]
    public void Allows_NormalNames(string name) =>
        Assert.False(Validators.IsReservedName(name));
}

public class FileTypeValidationTests
{
    [Theory]
    [InlineData(".exe")]
    [InlineData(".bat")]
    [InlineData(".ps1")]
    public void Blocks_DangerousExtensions(string extension) =>
        Assert.Throws<ValidationException>(() => Validators.ValidateFileType(extension, "application/octet-stream"));

    [Theory]
    [InlineData(".zip")]
    [InlineData(".pdf")]
    [InlineData(".png")]
    public void Allows_NormalExtensions(string extension) =>
        Validators.ValidateFileType(extension, "application/octet-stream");

    [Fact]
    public void Blocks_DangerousMimeTypes() =>
        Assert.Throws<ValidationException>(() => Validators.ValidateFileType(".bin", "application/x-msdownload"));
}

public class ShareOptionValidationTests
{
    [Fact]
    public void Rejects_PastExpiry() =>
        Assert.Throws<ValidationException>(
            () => Validators.ValidateShareOptions(DateTime.UtcNow.AddDays(-1), null));

    [Fact]
    public void Rejects_Expiry_OverOneYear() =>
        Assert.Throws<ValidationException>(
            () => Validators.ValidateShareOptions(DateTime.UtcNow.AddDays(400), null));

    [Fact]
    public void Rejects_BadDownloadLimits()
    {
        Assert.Throws<ValidationException>(() => Validators.ValidateShareOptions(null, 0));
        Assert.Throws<ValidationException>(() => Validators.ValidateShareOptions(null, -5));
        Assert.Throws<ValidationException>(() => Validators.ValidateShareOptions(null, 2_000_000));
    }

    [Fact]
    public void Accepts_ValidOptions()
    {
        Validators.ValidateShareOptions(DateTime.UtcNow.AddDays(7), 10);
        Validators.ValidateShareOptions(null, null);
    }
}

public class ShareLinkDomainTests
{
    private static ShareLink NewShare(DateTime? expiresAt = null, int? maxDownloads = null, bool active = true) => new()
    {
        FileId = Guid.NewGuid(),
        Token = "token",
        ExpiresAt = expiresAt,
        MaxDownloads = maxDownloads,
        DownloadCount = 0,
        IsActive = active,
        CreatedBy = Guid.NewGuid(),
    };

    [Fact]
    public void Usable_WhenActive_NoExpiry_NoLimit()
    {
        Assert.True(NewShare().IsUsable(DateTime.UtcNow));
    }

    [Fact]
    public void NotUsable_WhenRevoked()
    {
        Assert.False(NewShare(active: false).IsUsable(DateTime.UtcNow));
    }

    [Fact]
    public void NotUsable_WhenExpired()
    {
        Assert.False(NewShare(expiresAt: DateTime.UtcNow.AddMinutes(-1)).IsUsable(DateTime.UtcNow));
    }

    [Fact]
    public void Usable_UntilExpiry()
    {
        Assert.True(NewShare(expiresAt: DateTime.UtcNow.AddMinutes(5)).IsUsable(DateTime.UtcNow));
    }

    [Theory]
    [InlineData(0, true)]   // limit not yet reached
    [InlineData(1, false)]  // limit exhausted
    public void DownloadLimit_IsEvaluated(int used, bool expected)
    {
        var share = NewShare(maxDownloads: 1);
        share.DownloadCount = used;
        Assert.Equal(expected, share.IsUsable(DateTime.UtcNow));
    }
}
