using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerSemanticValidationTests
{
    private const string CanonicalDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void StartPreparedPlanRejectsZeroRuntimeGeneration()
    {
        AssertRejected(CreateRequestJson(
            "startPreparedPlan",
            new JsonObject
            {
                ["preparedPlanId"] = WrappedGuid(Guid.NewGuid()),
                ["expectedGeneration"] = WrappedLong(0),
            }));
    }

    [Fact]
    public void StartPreparedPlanRejectsEmptyPreparedPlanId()
    {
        AssertRejected(CreateRequestJson(
            "startPreparedPlan",
            new JsonObject
            {
                ["preparedPlanId"] = WrappedGuid(Guid.Empty),
                ["expectedGeneration"] = WrappedLong(1),
            }));
    }

    [Fact]
    public void PrepareBundleRejectsNonCanonicalRuntimeBundleDigest()
    {
        AssertRejected(CreateRequestJson(
            "prepareBundle",
            new JsonObject
            {
                ["bundleId"] = new JsonObject
                {
                    ["digest"] = WrappedString("sha256:ABCDEF"),
                },
            }));
    }

    [Fact]
    public void PreparePlanRejectsEmptyPreparedBundleId()
    {
        AssertRejected(CreateRequestJson(
            "preparePlan",
            new JsonObject
            {
                ["preparedBundleId"] = WrappedGuid(Guid.Empty),
                ["planHash"] = WrappedString(CanonicalDigest),
            }));
    }

    [Fact]
    public void PreparePlanRejectsNonCanonicalPlanHash()
    {
        AssertRejected(CreateRequestJson(
            "preparePlan",
            new JsonObject
            {
                ["preparedBundleId"] = WrappedGuid(Guid.NewGuid()),
                ["planHash"] = WrappedString("SHA256:0123"),
            }));
    }

    [Fact]
    public void StopGenerationRejectsZeroRuntimeGeneration()
    {
        AssertRejected(CreateRequestJson(
            "stopGeneration",
            new JsonObject
            {
                ["generation"] = WrappedLong(0),
                ["reason"] = 0,
            }));
    }

    [Fact]
    public void StopGenerationRejectsUndefinedReason()
    {
        AssertRejected(CreateRequestJson(
            "stopGeneration",
            new JsonObject
            {
                ["generation"] = WrappedLong(1),
                ["reason"] = 999,
            }));
    }

    [Fact]
    public void HelloRejectsShortClientNonce()
    {
        Guid appSessionId = Guid.NewGuid();
        AssertRejected(CreateHelloJson(appSessionId, appSessionId, new byte[31], new byte[32], 1, 0, 1, 0));
    }

    [Fact]
    public void HelloRejectsShortProof()
    {
        Guid appSessionId = Guid.NewGuid();
        AssertRejected(CreateHelloJson(appSessionId, appSessionId, new byte[32], new byte[31], 1, 0, 1, 0));
    }

    [Fact]
    public void HelloRejectsBodyAndEnvelopeAppSessionMismatch()
    {
        AssertRejected(CreateHelloJson(Guid.NewGuid(), Guid.NewGuid(), new byte[32], new byte[32], 1, 0, 1, 0));
    }

    [Fact]
    public void HelloRejectsInvertedProtocolRange()
    {
        Guid appSessionId = Guid.NewGuid();
        AssertRejected(CreateHelloJson(appSessionId, appSessionId, new byte[32], new byte[32], 1, 1, 1, 0));
    }

    [Fact]
    public void HelloRejectsRangeThatDoesNotContainEnvelopeProtocol()
    {
        Guid appSessionId = Guid.NewGuid();
        AssertRejected(CreateHelloJson(appSessionId, appSessionId, new byte[32], new byte[32], 2, 0, 2, 0));
    }

    private static void AssertRejected(string json)
    {
        BrokerProtocolDecodeResult result = BrokerProtocolCodec.DecodeRequest(Encoding.UTF8.GetBytes(json));

        Assert.NotEqual(BrokerProtocolDecodeStatus.Success, result.Status);
        Assert.Null(result.Envelope);
    }

    private static string CreateRequestJson(string kind, JsonObject body)
    {
        return new JsonObject
        {
            ["protocol"] = Protocol(1, 0),
            ["kind"] = kind,
            ["appSessionId"] = Guid.NewGuid().ToString(),
            ["brokerSessionId"] = Guid.NewGuid().ToString(),
            ["operationId"] = Guid.NewGuid().ToString(),
            ["sequence"] = 1,
            ["issuedAtUnixMs"] = 1789027200000,
            ["deadlineUnixMs"] = 1789027215000,
            ["body"] = body,
        }.ToJsonString();
    }

    private static string CreateHelloJson(
        Guid envelopeAppSessionId,
        Guid bodyAppSessionId,
        byte[] clientNonce,
        byte[] proof,
        ushort minimumMajor,
        ushort minimumMinor,
        ushort maximumMajor,
        ushort maximumMinor)
    {
        JsonObject body = new()
        {
            ["supportedProtocols"] = new JsonObject
            {
                ["minimum"] = Protocol(minimumMajor, minimumMinor),
                ["maximum"] = Protocol(maximumMajor, maximumMinor),
            },
            ["appSessionId"] = WrappedGuid(bodyAppSessionId),
            ["clientNonce"] = Convert.ToBase64String(clientNonce),
            ["proof"] = Convert.ToBase64String(proof),
        };

        return new JsonObject
        {
            ["protocol"] = Protocol(1, 0),
            ["kind"] = "hello",
            ["appSessionId"] = envelopeAppSessionId.ToString(),
            ["brokerSessionId"] = Guid.NewGuid().ToString(),
            ["operationId"] = Guid.NewGuid().ToString(),
            ["sequence"] = 1,
            ["issuedAtUnixMs"] = 1789027200000,
            ["deadlineUnixMs"] = 1789027215000,
            ["body"] = body,
        }.ToJsonString();
    }

    private static JsonObject Protocol(ushort major, ushort minor)
        => new()
        {
            ["major"] = major,
            ["minor"] = minor,
        };

    private static JsonObject WrappedGuid(Guid value)
        => new()
        {
            ["value"] = value.ToString(),
        };

    private static JsonObject WrappedLong(long value)
        => new()
        {
            ["value"] = value,
        };

    private static JsonObject WrappedString(string value)
        => new()
        {
            ["value"] = value,
        };
}
