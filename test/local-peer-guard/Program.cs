using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

if (args.Length != 1) throw new ArgumentException("Pass the built server assembly path");
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var type = assembly.GetType("GameApi", throwOnError: true)!;
var isLocal = type.GetMethod("IsLocal", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new Exception("Missing direct-peer predicate");
void Check(string name, string? address, string? forwarded, bool expected)
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = address == null ? null : IPAddress.Parse(address);
    if (forwarded != null) context.Request.Headers["X-Forwarded-For"] = forwarded;
    bool actual = (bool)isLocal.Invoke(null, new object[] { context })!;
    if (actual != expected) throw new Exception($"{name}: actual={actual} expected={expected}");
    Console.WriteLine($"PASS {name}");
}
Check("IPv4 localhost", "127.0.0.1", null, true);
Check("IPv4 loopback subnet", "127.0.0.2", null, true);
Check("IPv6 localhost", "::1", null, true);
Check("IPv4-mapped localhost", "::ffff:127.0.0.1", null, true);
Check("private direct peer", "10.0.0.15", null, false);
Check("public direct peer", "203.0.113.7", null, false);
Check("missing direct peer", null, null, false);
Check("spoofed forwarded loopback", "203.0.113.7", "127.0.0.1", false);
Check("loopback proxy remains allowed", "127.0.0.1", "203.0.113.7", true);
Console.WriteLine("PASS direct-peer guard is not public reverse proxy protection");

// Exercise the actual mapped endpoints with synthetic HttpContext objects. No
// app.Start()/Run() is called and no listener or socket is opened.
Environment.SetEnvironmentVariable("SNAKE_ENV", "development");
Environment.SetEnvironmentVariable("SNAKE_LOCAL_STRENGTH_EVAL", "1");
Environment.SetEnvironmentVariable("SNAKE_DATA_FILE", Path.Combine(Path.GetTempPath(), $"snake-peer-guard-{Guid.NewGuid():N}.json"));
Environment.SetEnvironmentVariable("SNAKE_SQLITE_FILE", null);
var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions
{
    ApplicationName = assembly.GetName().Name,
    EnvironmentName = "Development"
});
builder.Logging.ClearProviders();
await using var app = builder.Build();
var map = type.GetMethod("Map", BindingFlags.Public | BindingFlags.Static)
    ?? throw new Exception("Missing endpoint mapping");
map.Invoke(null, new object[] { app, Path.GetTempPath() });
var sources = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources;
var endpoints = sources.SelectMany(source => source.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().ToArray();
foreach (var path in new[] { "/api/v3/internal/local-strength-pve/start", "/api/v3/internal/local-strength-pve/verify" })
{
    var endpoint = endpoints.SingleOrDefault(item => item.RoutePattern.RawText == path)
        ?? throw new Exception("Endpoint not registered: " + path);
    async Task CheckRoute(string? address, string? forwarded, int expected)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = app.Services;
        context.Connection.RemoteIpAddress = address == null ? null : IPAddress.Parse(address);
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(new BodyDetection());
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
        context.Request.ContentLength = 2;
        if (forwarded != null) context.Request.Headers["X-Forwarded-For"] = forwarded;
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        if (context.Response.StatusCode != expected)
            throw new Exception($"{path} peer={address} expected={expected} actual={context.Response.StatusCode}");
    }
    await CheckRoute("203.0.113.7", "127.0.0.1", 403);
    await CheckRoute(null, null, 403);
    await CheckRoute("127.0.0.1", "203.0.113.7", 401);
    await CheckRoute("::1", null, 401);
    Console.WriteLine($"PASS endpoint direct-peer guard {path}");
}

sealed class BodyDetection : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature
{
    public bool CanHaveBody => true;
}
