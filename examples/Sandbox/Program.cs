// End to end in the sandbox with a test_ key: fake money in, a simulated deposit, the webhook log.
// Run with: OBLODAI_PUBLIC_ID=test_… OBLODAI_SECRET=… dotnet run --project examples/Sandbox
using Oblodai;
using Oblodai.Examples;

using var oblodai = new OblodaiClient();
await SandboxExample.RunAsync(oblodai, Console.Out);
