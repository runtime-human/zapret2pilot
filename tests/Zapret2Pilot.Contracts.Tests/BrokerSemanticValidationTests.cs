using System.Text;
using Xunit;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Contracts.Tests;

public sealed class BrokerSemanticValidationTests
{
    private const string CanonicalDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void StartPreparedPlanRejectsZeroRuntimeGeneration()
    {
        Guid planId = Guid.NewGuid();
        AssertRejected(CreateRequestJson(
            "startPreparedPlan",
            $$"""{"preparedPlanId":{"value":"{{planId}}"},"expectedGeneration":{"value":0}}"""));
    }

    [Fact]
    public void StartPreparedPlanRejectsEmptyPreparedPlanId()
    {
        AssertRejected(CreateRequestJson(
            "startPreparedPlan",
            """{"preparedPlanId":{"value":"00000000-0000-0000-0000-000000000000"},"expectedGeneration":{"value":1}}"""));
    }

    [Fact]
    public void PrepareBundleRejectsNonCanonicalRuntimeBundleDigest()
    {
        AssertRejected(CreateRequestJson(
            "prepareBundle",
            """{"bundleId":{"digest":{"value":"sha256:ABCDEF"}}}"""));
    }

    [Fact]
    public void PreparePlanRejectsEmptyPreparedBundleId()
    {
        AssertRejected(CreateRequestJson(
            "preparePlan",
            $$"""{"preparedBundleId":{"value":"00000000-0000-0000-0000-000000000000"},"planHash":{"value":"{{CanonicalDigest}}"}}"""));
    }

    [Fact]
    public void PreparePlanRejectsNonCanonicalPlanHash()
    {
        Guid preparedBundleId = Guid.NewGuid();
        AssertRejected(CreateRequestJson(
            "preparePlan",
            $$"""{"preparedBundleId":{"value":"{{preparedBundleId}}"},"planHash":{"value":"SHA256:0123"}}"""));
    }

    [Fact]
    public void StopGenerationRejectsZeroRuntimeGeneration()
    {
        AssertRejected(CreateRequestJson(
            "stopGeneration",
            """{"generation":{"value":0},"reason":0}"""));
    }

    [Fact]
    public void StopGenerationRejectsUndefinedReason()
    {
        AssertRejected(CreateRequestJson(
            "stopGeneration",
            """{"generation":{"value":1},"reason":999}"""));
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

    private static string CreateRequestJson(string kind, string body)
    {
        Guid appSessionId = Guid.NewGuid();
        return $$"""
            {
              "protocol":{"major":1,"minor":0},
              "kind":"{{kind}}",
              "appSessionId":"{{appSessionId}}",
              "brokerSessionId":"{{Guid.NewGuid()}}",
              "operationId":"{{Guid.NewGuid()}}",
              "sequence":1,
              "issuedAtUnixMs":1789027200000,
              "deadlineUnixMs":1789027215000,
              "body":{{body}}
            }
            """;
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
        return $$"""
            {
              "protocol":{"major":1,"minor":0},
              "kind":"hello",
              "appSessionId":"{{envelopeAppSessionId}}",
              "brokerSessionId":"{{Guid.NewGuid()}}",
              "operationId":"{{Guid.NewGuid()}}",
              "sequence":1,
              "issuedAtUnixMs":1789027200000,
              "deadlineUnixMs":1789027215000,
              "body":{
                "supportedProtocols":{
                  "minimum":{"major":{{minimumMajor}},"minor":{{minimumMinor}}},
                  "maximum":{"major":{{maximumMajor}},"minor":{{maximumMinor}}}
                },
                "appSessionId":{"value":"{{bodyAppSessionId}}"},
                "clientNonce":"{{Convert.ToBase64String(clientNonce)}}",
                "proof":"{{Convert.ToBase64String(proof)}}"
              }
            }
            """;
    }
}
