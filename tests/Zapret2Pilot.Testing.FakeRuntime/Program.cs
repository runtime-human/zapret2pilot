using System;
using System.Threading;
using System.Threading.Tasks;

// Fake Zapret2 runtime used by RuntimeProcessHost tests (milestone 0.0.17).
//
// Lifecycle contract (intentionally minimal):
//   1. Print a single "READY" line on stdout and flush it so a parent
//      that captures the standard output can read it deterministically.
//   2. Block indefinitely until a Ctrl+C / SIGINT is received.
//   3. Exit cleanly with code 0 on shutdown.
//
// The fake must NOT reference any production code: it is a test helper
// that stands in for the real winws2.exe and exists solely so that
// RuntimeProcessHost has something to launch in unit/integration tests.

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, args) =>
{
    // Suppress the default SIGINT termination so we can shut down
    // deterministically with exit code 0.
    args.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine("READY");
Console.Out.Flush();

try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C / SIGINT.
}

return 0;
