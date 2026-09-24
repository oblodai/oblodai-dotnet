// Create an invoice, show the payer the address/URL, then poll until it is final.
// Run with: OBLODAI_PUBLIC_ID=… OBLODAI_SECRET=… dotnet run --project examples/AcceptPayment
using Oblodai;
using Oblodai.Examples;

using var oblodai = new OblodaiClient(); // credentials come from OBLODAI_PUBLIC_ID / OBLODAI_SECRET
await AcceptPaymentExample.RunAsync(oblodai, Console.Out, TimeSpan.FromSeconds(10));
