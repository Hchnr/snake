#nullable enable
using System.Net.Http.Headers;
using System.Text.Json;

// Server-side only. No credentials, response body or platform session key are
// exposed to the caller or logged. The endpoint is fixed by the platform.
sealed class DouyinSessionExchange
{
    private const string Endpoint = "https://minigame.zijieapi.com/mgplatform/api/apps/jscode2session";
    private const int MaxResponseBytes = 16 * 1024;
    private readonly HttpClient http;
    private readonly TimeSpan timeout;

    public DouyinSessionExchange(HttpClient http, TimeSpan? timeout = null)
    {
        this.http = http;
        this.timeout = timeout ?? TimeSpan.FromSeconds(8);
    }

    public async Task<ExchangeResult> Exchange(string appId, string secret, string? code, string? anonymousCode, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(secret)) return new(503, "抖音登录尚未配置", null, null);
        if ((string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(anonymousCode)) ||
            (code?.Length ?? 0) > 4096 || (anonymousCode?.Length ?? 0) > 4096)
            return new(400, "登录凭证无效", null, null);
        string url = Endpoint + "?appid=" + Uri.EscapeDataString(appId) + "&secret=" + Uri.EscapeDataString(secret);
        if (!string.IsNullOrEmpty(code)) url += "&code=" + Uri.EscapeDataString(code);
        if (!string.IsNullOrEmpty(anonymousCode)) url += "&anonymous_code=" + Uri.EscapeDataString(anonymousCode);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return Unavailable();
            if (response.Content.Headers.ContentLength > MaxResponseBytes) return Malformed();
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + count > MaxResponseBytes) return Malformed();
                buffer.Write(chunk, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Number || !error.TryGetInt64(out long errorCode)) return Malformed();
            if (errorCode != 0)
                return errorCode is 40018 or 40019 ? new(401, "抖音登录凭证无效或已使用", null, null) : Unavailable();
            if (!ReadId(root, "openid", out var openId) || !ReadId(root, "anonymous_openid", out var anonymousId)) return Malformed();
            openId = string.IsNullOrEmpty(openId) ? anonymousId : openId;
            if (string.IsNullOrEmpty(openId)) return new(401, "抖音登录没有返回账号标识", null, null);
            return new(200, "", openId, anonymousId);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidOperationException)
        {
            return Unavailable();
        }
    }

    private static bool ReadId(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return value == null || (value.Length <= 256 && (value.Length == 0 || !string.IsNullOrWhiteSpace(value)));
    }

    private static ExchangeResult Unavailable() => new(503, "抖音登录服务暂不可用", null, null);
    private static ExchangeResult Malformed() => new(503, "抖音登录结果无法读取", null, null);
}

sealed record ExchangeResult(int Status, string Error, string? OpenId, string? AnonymousId);
