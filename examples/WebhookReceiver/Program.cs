// A webhook receiver on the BCL's HttpListener; the rules live in Example.cs.
// Run with: OBLODAI_WEBHOOK_SECRET=… dotnet run --project examples/WebhookReceiver
using System.Net;
using Oblodai.Examples;

var secret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_SECRET")
             ?? throw new InvalidOperationException("set OBLODAI_WEBHOOK_SECRET");
var previousSecret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_PREVIOUS_SECRET"); // during a rotation
var receiver = new WebhookReceiverExample(secret, previousSecret, Console.Out);

using var listener = new HttpListener();
listener.Prefixes.Add("http://127.0.0.1:8096/");
listener.Start();
Console.WriteLine("listening on http://127.0.0.1:8096/hook");

while (listener.IsListening)
{
    var context = await listener.GetContextAsync();
    using var response = context.Response;
    using var buffer = new MemoryStream();
    await context.Request.InputStream.CopyToAsync(buffer);
    response.StatusCode = (int)receiver.Handle(buffer.ToArray(), name => context.Request.Headers[name]);
}
