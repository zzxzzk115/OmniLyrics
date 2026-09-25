using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Core;

Environment.SetEnvironmentVariable("CIDER_AUTH_MODE", "none");
using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
var state = new PlayerState { Title = "Test song", Artists = ["Test artist"], SourceApp = "Cider" };
var id = "one"; var rating = 0; var writes = 0; var reads = 0; var failed = false; var rejectWrite = false;
var delayConfirmation = false; var flipTrackAfterRead = false; var lastRequested = -99;
var host = Task.Run(async () =>
{
    while (server.IsListening)
    {
        HttpListenerContext context;
        try { context = await server.GetContextAsync(); } catch { break; }
        var path = context.Request.Url!.AbsolutePath;
        object data;
        if (path.EndsWith("/now-playing"))
        {
            data = new { name = "Test song", artistName = "Test artist", playParams = new { id, kind = "song" } };
            if (flipTrackAfterRead) { flipTrackAfterRead = false; id = "two"; }
        }
        else if (path.EndsWith("/status"))
        {
            reads++;
            if (failed) context.Response.StatusCode = 403;
            if (delayConfirmation && reads % 3 == 0) { rating = lastRequested; delayConfirmation = false; }
            data = new { rating };
        }
        else
        {
            writes++;
            if (context.Request.HttpMethod != "PUT") throw new Exception("Favorite mutation must be explicit PUT");
            using var body = await JsonDocument.ParseAsync(context.Request.InputStream);
            lastRequested = body.RootElement.GetProperty("rating").GetInt32();
            if (rejectWrite) context.Response.StatusCode = 403;
            else if (!delayConfirmation) rating = lastRequested;
            data = new { };
        }
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.OutputStream, new { data });
        context.Response.Close();
    }
});
using var api = new CiderFavorites($"http://127.0.0.1:{port}");
var passed = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var snapshot = await api.GetFavoriteAsync(state);
Check("Favorite capability is read without any write", snapshot is { IsFavorite: false } && writes == 0);
var confirmed = await api.SetFavoriteAsync(state, snapshot!, true);
Check("Favorite writes the requested rating and confirms it", confirmed?.IsFavorite == true && writes == 1 && lastRequested == 1);
id = "two";
Check("Stale track IDs are rejected before writing", await api.SetFavoriteAsync(state, snapshot!, false) == null && writes == 1);
flipTrackAfterRead = true; id = "one";
Check("Track changes during status reads produce no stale state", await api.GetFavoriteAsync(state) == null);
id = "one"; failed = true;
Check("Missing library permission hides capability", await api.GetFavoriteAsync(state) == null && writes == 1);
failed = false; rejectWrite = true;
Check("Rejected favorite writes do not report success", await api.SetFavoriteAsync(state, snapshot!, false) == null && rating == 1);
rejectWrite = false; delayConfirmation = true;
Check("Dispatch acknowledgement waits for confirmed rating", (await api.SetFavoriteAsync(state, snapshot!, false))?.IsFavorite == false && rating == 0);
state.Title = "New song";
Check("Changed metadata cannot mutate the previous track", await api.SetFavoriteAsync(state, snapshot!, true) == null && writes == 3);
server.Stop(); await host;
Console.WriteLine($"{passed} favorites checks passed");
