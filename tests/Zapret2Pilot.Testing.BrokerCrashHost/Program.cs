using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Zapret2Pilot.Broker.Hosting;
using Zapret2Pilot.Broker.Runtime;
using Zapret2Pilot.Contracts.Identity;
using Zapret2Pilot.Contracts.Protocol;
using Zapret2Pilot.Contracts.Transport;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Assets;
using Zapret2Pilot.Infrastructure.FileSystem;
using Zapret2Pilot.Runtime.Hosting;
using Zapret2Pilot.Runtime.Integrity;
using Zapret2Pilot.Runtime.Kernel;
using Zapret2Pilot.Runtime.Ownership;
using ContractGeneration = Zapret2Pilot.Contracts.Identity.RuntimeGeneration;

if (args.Length != 2)
{
    return 2;
}

string root = Path.GetFullPath(args[0]);
string readyFile = Path.GetFullPath(args[1]);

try
{
    AppDataLayout layout = new(root);
    layout.EnsureCreated();

    RuntimeProcessStartContext context =
        PrepareFakeRuntime(layout.RuntimeDirectory);
    PreparedPlanId preparedPlanId = PreparedPlanId.New();

    HostApplicationBuilder builder =
        BrokerHostBuilder.CreateBuilder([]);

    builder.Services.Replace(
        ServiceDescriptor.Singleton(layout));
    builder.Services.Replace(
        ServiceDescriptor.Singleton(
            new RuntimeOwnershipMutex(
                $"Z2P_BROKER_CRASH_TEST_{Guid.NewGuid():N}")));
    builder.Services.Replace(
        ServiceDescriptor.Singleton<IPreparedRuntimePlanResolver>(
            new SinglePreparedPlanResolver(
                preparedPlanId,
                context)));

    using IHost host = builder.Build();
    await host.StartAsync();

    BrokerRuntimeDispatcher dispatcher =
        host.Services.GetRequiredService<BrokerRuntimeDispatcher>();
    RuntimeKernelLoop kernel =
        host.Services.GetRequiredService<RuntimeKernelLoop>();

    DateTimeOffset now = DateTimeOffset.UtcNow;
    BrokerRequestEnvelope startRequest = new(
        BrokerProtocolVersion.V1,
        AppSessionId.New(),
        BrokerSessionId.New(),
        BrokerOperationId.New(),
        new RequestSequence(1),
        now,
        now + BrokerProtocolLimits.MaxRequestLifetime,
        new StartPreparedPlanRequest(
            preparedPlanId,
            new ContractGeneration(
                kernel.CurrentState.Generation.Value)));

    BrokerResponseEnvelope start =
        await dispatcher.DispatchAsync(startRequest);

    if (start.Status != BrokerResponseStatus.Accepted
        || kernel.CurrentState.LastStartResult is null)
    {
        await File.WriteAllTextAsync(
            readyFile,
            $"ERROR:Start:{start.Status}");
        return 3;
    }

    await File.WriteAllTextAsync(
        readyFile,
        kernel.CurrentState.LastStartResult.ProcessId
            .ToString(
                System.Globalization.CultureInfo.InvariantCulture));

    // The parent test intentionally terminates this process without graceful
    // Host disposal. The RuntimeProcessHost Job Object handle must therefore
    // close as part of OS process teardown and kill FakeRuntime.
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}
catch (Exception ex)
{
    try
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(readyFile) ?? root);
        await File.WriteAllTextAsync(
            readyFile,
            $"ERROR:{ex.GetType().Name}:{ex.Message}");
    }
    catch
    {
        // The parent process will also observe this helper exiting.
    }

    return 4;
}

static RuntimeProcessStartContext PrepareFakeRuntime(
    string runtimeDirectory)
{
    const string FakeRuntimeExecutableName =
        "Zapret2Pilot.Testing.FakeRuntime.exe";
    const string ManifestExecutableRelativePath =
        "bin/fake-runtime.exe";

    string sourceExecutable = Path.Combine(
        AppContext.BaseDirectory,
        FakeRuntimeExecutableName);

    if (!File.Exists(sourceExecutable))
    {
        throw new FileNotFoundException(
            "FakeRuntime apphost is missing.",
            sourceExecutable);
    }

    string binDirectory =
        Path.Combine(runtimeDirectory, "bin");
    Directory.CreateDirectory(binDirectory);

    string destinationExecutable = Path.Combine(
        runtimeDirectory,
        ManifestExecutableRelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar));

    File.Copy(
        sourceExecutable,
        destinationExecutable,
        overwrite: true);

    const string FakeRuntimeBaseName =
        "Zapret2Pilot.Testing.FakeRuntime";

    foreach (string extension in new[]
             {
                 ".dll",
                 ".deps.json",
                 ".runtimeconfig.json",
             })
    {
        string source = Path.Combine(
            AppContext.BaseDirectory,
            FakeRuntimeBaseName + extension);

        if (File.Exists(source))
        {
            File.Copy(
                source,
                Path.Combine(
                    binDirectory,
                    FakeRuntimeBaseName + extension),
                overwrite: true);
        }
    }

    string hash = Convert.ToHexString(
        SHA256.HashData(
            File.ReadAllBytes(destinationExecutable)))
        .ToLowerInvariant();

    ZapretAssetManifest manifest = new(
        new ZapretRuntimeAsset(
            ManifestExecutableRelativePath,
            hash,
            AssetKind.Executable),
        Array.Empty<ZapretRuntimeAsset>(),
        Array.Empty<ZapretRuntimeAsset>());

    ZapretAssetVerificationSummary summary = new(
        [ManifestExecutableRelativePath]);

    Result<VerifiedRuntimeExecutablePath> verified =
        VerifiedRuntimeExecutablePath.TryCreate(
            manifest,
            summary,
            runtimeDirectory);

    if (verified.IsFailure)
    {
        throw new InvalidOperationException(
            verified.Error.ToString());
    }

    CompiledZapretPlan plan = new(
        generatedConfigContent: "# fake config\n",
        argsContent: "--new\n",
        hostlists:
            Array.Empty<CompiledZapretPlan.HostlistContent>(),
        id: null,
        profileId: null,
        commandLine: null,
        arguments: null,
        cacheKey: new RuntimePlanCacheKey(
            new string('b', 64)));

    return new(
        plan,
        manifest,
        runtimeDirectory,
        verified.Value);
}

file sealed class SinglePreparedPlanResolver :
    IPreparedRuntimePlanResolver
{
    private readonly PreparedPlanId planId;
    private readonly RuntimeProcessStartContext context;

    public SinglePreparedPlanResolver(
        PreparedPlanId planId,
        RuntimeProcessStartContext context)
    {
        this.planId = planId;
        this.context = context;
    }

    public bool TryResolve(
        PreparedPlanId preparedPlanId,
        out RuntimeProcessStartContext? startContext)
    {
        if (preparedPlanId == planId)
        {
            startContext = context;
            return true;
        }

        startContext = null;
        return false;
    }
}
