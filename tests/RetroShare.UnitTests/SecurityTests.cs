using RetroShare.Infrastructure.Security;
using Xunit;

namespace RetroShare.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_And_Verify_Roundtrip_Succeeds()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.NotEqual("correct horse battery staple", hash);
        Assert.True(_hasher.Verify(hash, "correct horse battery staple"));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var hash = _hasher.Hash("hunter2hunter2");

        Assert.False(_hasher.Verify(hash, "hunter2hunter3"));
    }

    [Fact]
    public void Hash_IsSalted_EveryCallDiffers()
    {
        Assert.NotEqual(_hasher.Hash("same-input"), _hasher.Hash("same-input"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("v9$100000$AAAA$AAAA")]
    public void Verify_MalformedHash_FailsSafely(string stored)
    {
        Assert.False(_hasher.Verify(stored, "anything"));
    }

    [Fact]
    public void Verify_NullInputs_False()
    {
        Assert.False(_hasher.Verify(null!, "x"));
        Assert.False(_hasher.Verify("v1$1000$AAAA$AAAA", null!));
    }
}

public class SecureTokenGeneratorTests
{
    private readonly SecureTokenGenerator _generator = new();

    [Fact]
    public void Tokens_Are_Unique_And_UrlSafe()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => _generator.GenerateToken(32)).ToHashSet();

        Assert.Equal(200, tokens.Count);
        Assert.All(tokens, token => Assert.Matches(@"^[A-Za-z0-9_-]+$", token));
    }

    [Fact]
    public void HashToken_IsDeterministic_ForSameInput_AndDifferent_ForOthers()
    {
        var first = _generator.HashToken("token-value");
        var second = _generator.HashToken("token-value");
        var other = _generator.HashToken("different-value");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }
}
