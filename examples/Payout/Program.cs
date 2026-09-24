// Validate first (free, no side effects), then create with your own idempotency key.
// Run with: OBLODAI_PUBLIC_ID=… OBLODAI_SECRET=… dotnet run --project examples/Payout
using Oblodai;
using Oblodai.Examples;

// The same API key that takes payments sends them out again; the client reads it from the environment.
using var oblodai = new OblodaiClient();
await PayoutExample.RunAsync(oblodai, Console.Out);
