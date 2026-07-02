using System.Net;
using System.Net.Sockets;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRouting();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok("ok"));

app.MapGet("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var udpClient = new UdpClient();
    var serverEp = new IPEndPoint(IPAddress.Loopback, 5400);

    // Start a receiving loop for UDP -> WebSocket
    var receiving = Task.Run(async () =>
    {
        while (true)
        {
            var result = await udpClient.ReceiveAsync();
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(result.Buffer), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch
            {
                break;
            }
        }
    });

    // WebSocket -> UDP
    var buffer = new byte[8192];
    while (ws.State == WebSocketState.Open)
    {
        var recv = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        if (recv.MessageType == WebSocketMessageType.Close)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            break;
        }

        if (recv.Count > 0)
        {
            var data = new byte[recv.Count];
            Array.Copy(buffer, data, recv.Count);
            await udpClient.SendAsync(data, data.Length, serverEp);
        }
    }

    try { udpClient.Dispose(); } catch { }
    await receiving;
});

app.Run();
