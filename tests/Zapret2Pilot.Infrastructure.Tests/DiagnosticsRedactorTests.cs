using Xunit;
using Zapret2Pilot.Infrastructure.Diagnostics;

namespace Zapret2Pilot.Infrastructure.Tests;

public sealed class DiagnosticsRedactorTests
{
    [Fact]
    public static void RedactRemovesTokenValue()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact("request token=abc123 status=ok");

        Assert.Equal("request token=<redacted> status=ok", redacted);
        Assert.DoesNotContain("abc123", redacted);
    }

    [Fact]
    public static void RedactRemovesApiKeyValue()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact("api_key=secret-value");

        Assert.Equal("api_key=<redacted>", redacted);
        Assert.DoesNotContain("secret-value", redacted);
    }

    [Fact]
    public static void RedactRemovesPasswordValue()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact("password=super-secret");

        Assert.Equal("password=<redacted>", redacted);
        Assert.DoesNotContain("super-secret", redacted);
    }

    [Fact]
    public static void RedactRemovesBearerToken()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9");

        Assert.Equal("Authorization: Bearer <redacted>", redacted);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted);
    }

    [Fact]
    public static void RedactRemovesWindowsUserNameFromPath()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact(@"file=C:\Users\Alice\AppData\Local\Zapret2Pilot\log.txt");

        Assert.Equal(@"file=C:\Users\<redacted>\AppData\Local\Zapret2Pilot\log.txt", redacted);
        Assert.DoesNotContain("Alice", redacted);
    }

    [Fact]
    public static void RedactRemovesUrlQueryData()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact("url=https://example.com/watch?v=abc123&list=secret");

        Assert.Equal("url=https://example.com/watch?<redacted>", redacted);
        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("secret", redacted);
    }

    [Fact]
    public static void RedactRemovesUrlQueryAndFragmentData()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact("url=https://example.com/watch?v=abc123#section-secret");

        Assert.Equal("url=https://example.com/watch?<redacted>#<redacted>", redacted);
        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("section-secret", redacted);
    }

    [Fact]
    public static void RedactReturnsOriginalTextWhenNoSensitiveData()
    {
        DiagnosticsRedactor redactor = new();

        string redacted = redactor.Redact("diagnostics completed without sensitive values");

        Assert.Equal("diagnostics completed without sensitive values", redacted);
    }
}
