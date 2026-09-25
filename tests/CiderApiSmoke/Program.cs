using System.Net;
using System.Net.Sockets;
using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Core.Configuration;

// Real HTTP transport against a local mock, with no external NuGet test packages.
using var reservation = new TcpListener(IPAddress.Loopback, 0);
reservation.Start();
var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
reservation.Stop();
using var listener = new HttpListener();
var baseUrl = $"http://127.0.0.1:{port}";
listener.Prefixes.Add(baseUrl + "/");
listener.Start();
var tokenPath = Path.GetTempFileName();
var originalToken = Environment.GetEnvironmentVariable("CIDER_API_TOKEN");
var originalPath = Environment.GetEnvironmentVariable("CIDER_TOKEN_FILE");
var originalDirectory = Environment.GetEnvironmentVariable("OMNILYRICS_CONFIG_DIR");
var originalMode = Environment.GetEnvironmentVariable("CIDER_AUTH_MODE");
var configDirectory = Path.Combine(Path.GetTempPath(), "omnilyrics-config-test-" + Guid.NewGuid().ToString("N"));
var count = 0;

async Task Check(string name, string? expected, string? explicitToken = null, int status = 200)
{
    using var client = new CiderV3Api(baseUrl, explicitToken);
    var requestTask = client.TryGetActiveAsync();
    var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(3));
    var actual = context.Request.Headers["apptoken"];
    var path = context.Request.Url?.AbsolutePath;
    context.Response.StatusCode = status;
    context.Response.Close();
    var available = await requestTask;
    if (actual != expected || path != "/api/v1/playback/active" || available != (status == 200))
        throw new Exception($"Failed: {name}"); // Never include credential values.
    Console.WriteLine($"PASS {name}");
    count++;
}

async Task CheckQueue(string name, string queueJson, int expectedCount, string? expectedTitle = null, int status = 200)
{
    using var client = new CiderV3Api(baseUrl, "queue-test");
    var task = client.GetUpcomingTracksAsync(2);
    foreach (var endpoint in new[] { "now-playing", "queue" })
    {
        var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (context.Request.HttpMethod != "GET" || context.Request.Url?.AbsolutePath != "/api/v1/playback/" + endpoint
            || context.Request.Headers["apptoken"] != "queue-test") throw new Exception("Queue must be read-only and authenticated");
        context.Response.StatusCode = endpoint == "queue" ? status : 200;
        var body = System.Text.Encoding.UTF8.GetBytes(endpoint == "queue" ? queueJson :
            "{\"info\":{\"name\":\"Current\",\"playParams\":{\"id\":\"current\"}}}");
        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }
    var result = await task;
    if (result.Count != expectedCount || expectedTitle != null && result[0].Title != expectedTitle)
        throw new Exception(name);
    Console.WriteLine("PASS " + name); count++;
}

try
{
    Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", configDirectory);
    Environment.SetEnvironmentVariable("CIDER_AUTH_MODE", null);
    Environment.SetEnvironmentVariable("CIDER_API_TOKEN", null);
    Environment.SetEnvironmentVariable("CIDER_TOKEN_FILE", null);
    await Check("tokenless local API", null);
    await Check("explicit token", "explicit-test", "explicit-test");
    Environment.SetEnvironmentVariable("CIDER_API_TOKEN", "environment-test");
    await Check("environment token", "environment-test");
    await Check("explicit token takes precedence", "explicit-test", "explicit-test");
    await File.WriteAllTextAsync(tokenPath, "file-test\n");
    Environment.SetEnvironmentVariable("CIDER_TOKEN_FILE", tokenPath);
    await Check("environment takes precedence over file", "environment-test");
    Environment.SetEnvironmentVariable("CIDER_API_TOKEN", null);
    await Check("token file with trailing newline", "file-test");
    await Check("authorization failure is unavailable", "file-test", status: 403);
    File.Delete(tokenPath);
    await Check("missing optional token file", null);
    await Check("multiline token never becomes headers", null, "test\nextra-header");
    Environment.SetEnvironmentVariable("CIDER_TOKEN_FILE", null);
    UserConfiguration.SaveCider("token", "shared-file-test");
    await Check("shared saved token", "shared-file-test");
    var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(UserConfiguration.SettingsPath))!;
    document["futureSetting"] = "preserve-me";
    File.WriteAllText(UserConfiguration.SettingsPath, document.ToJsonString());
    UserConfiguration.SaveCider("none");
    Environment.SetEnvironmentVariable("CIDER_API_TOKEN", "environment-test");
    await Check("saved none mode suppresses all token sources", null);
    Environment.SetEnvironmentVariable("CIDER_AUTH_MODE", "token");
    await Check("explicit authentication environment override", "environment-test");
    Environment.SetEnvironmentVariable("CIDER_AUTH_MODE", null);
    Environment.SetEnvironmentVariable("CIDER_API_TOKEN", null);
    UserConfiguration.SaveCider("token");
    await Check("switch back retains saved credential", "shared-file-test");
    if (!File.ReadAllText(UserConfiguration.SettingsPath).Contains("preserve-me")
        || File.ReadAllText(UserConfiguration.SettingsPath).Contains("shared-file-test"))
        throw new Exception("Settings preservation or credential separation failed.");
    if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(UserConfiguration.TokenPath)
        != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
        throw new Exception("Token permissions must be 0600.");
    Console.WriteLine("PASS settings preservation, separate credentials and private permissions");
    count++;
    var queue = """
        [{"id":"past","attributes":{"name":"Past","artistName":"Artist"}},
         {"id":"current","attributes":{"name":"Current","artistName":"Artist"}},
         {"id":"next","attributes":{"name":"Next","artistName":"Artist","albumName":"Album","durationInMillis":200000}},
         {"id":"then","attributes":{"name":"Then","artistName":"Artist"}},
         {"id":"last","attributes":{"name":"Last","artistName":"Artist"}}]
        """;
    await CheckQueue("Queue skips history and current track and limits upcoming tracks", queue, 2, "Next");
    await CheckQueue("Ambiguous repeated current IDs do not predict the wrong queue", queue.Replace("\"past\"", "\"current\""), 0);
    await CheckQueue("Missing current track is safely ignored", queue.Replace("\"current\"", "\"missing\""), 0);
    await CheckQueue("Queue authorization failure leaves playback unaffected", "{}", 0, status: 403);
    await CheckQueue("Malformed queue response is ignored", "not-json", 0);
    File.WriteAllText(UserConfiguration.SettingsPath, "invalid-json");
    try
    {
        UserConfiguration.SaveCider("token", "replacement-must-not-be-written");
        throw new Exception("Invalid configuration was overwritten.");
    }
    catch (System.Text.Json.JsonException)
    {
        if (File.ReadAllText(UserConfiguration.TokenPath).Trim() != "shared-file-test")
            throw new Exception("Credential changed after invalid settings.");
    }
    Console.WriteLine("PASS malformed configuration preserves existing credential");
    count++;
    Console.WriteLine($"{count} Cider API checks passed.");
}
finally
{
    Environment.SetEnvironmentVariable("CIDER_API_TOKEN", originalToken);
    Environment.SetEnvironmentVariable("CIDER_TOKEN_FILE", originalPath);
    Environment.SetEnvironmentVariable("OMNILYRICS_CONFIG_DIR", originalDirectory);
    Environment.SetEnvironmentVariable("CIDER_AUTH_MODE", originalMode);
    File.Delete(tokenPath);
    listener.Stop();
    if (Directory.Exists(configDirectory)) Directory.Delete(configDirectory, recursive: true);
}
