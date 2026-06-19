using System;

namespace Zapret2Pilot.Runtime.Windows;

public sealed record class RuntimeJobObjectCreateResult
{
    private RuntimeJobObjectCreateResult(
        bool created,
        bool unsupportedPlatform,
        IRuntimeJobObject? jobObject)
    {
        if (created == unsupportedPlatform)
        {
            throw new ArgumentException("Job object create result must be either created or unsupported.", nameof(created));
        }

        if (created && jobObject is null)
        {
            throw new ArgumentException("Created job object result must include a job object.", nameof(jobObject));
        }

        if (!created && jobObject is not null)
        {
            throw new ArgumentException("Unsupported job object result must not include a job object.", nameof(jobObject));
        }

        Created = created;
        UnsupportedPlatform = unsupportedPlatform;
        JobObject = jobObject;
    }

    public bool Created { get; }

    public bool UnsupportedPlatform { get; }

    public IRuntimeJobObject? JobObject { get; }

    public static RuntimeJobObjectCreateResult CreatedJobObject(IRuntimeJobObject jobObject)
    {
        ArgumentNullException.ThrowIfNull(jobObject);

        return new RuntimeJobObjectCreateResult(
            created: true,
            unsupportedPlatform: false,
            jobObject: jobObject);
    }

    public static RuntimeJobObjectCreateResult UnsupportedPlatformResult()
    {
        return new RuntimeJobObjectCreateResult(
            created: false,
            unsupportedPlatform: true,
            jobObject: null);
    }
}
