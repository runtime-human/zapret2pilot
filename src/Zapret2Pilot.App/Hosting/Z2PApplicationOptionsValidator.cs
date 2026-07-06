using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Zapret2Pilot.App.Hosting;

public sealed class Z2PApplicationOptionsValidator : IValidateOptions<Z2PApplicationOptions>
{
    public ValidateOptionsResult Validate(string? name, Z2PApplicationOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.StorageDatabasePath))
            failures.Add("StorageDatabasePath is required.");

        if (string.IsNullOrWhiteSpace(options.TrustedTufRoot))
            failures.Add("TrustedTufRoot is required.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
