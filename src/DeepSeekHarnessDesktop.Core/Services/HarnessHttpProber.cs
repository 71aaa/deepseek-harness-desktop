using System.Text;

namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>对 http://127.0.0.1:3080 的默认 HTTP 探测实现（GET + 有界读取正文）。</summary>
public static class HarnessHttpProber
{
    private const int MaxBodyBytes = 256 * 1024;

    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(2),
    })
    {
        Timeout = TimeSpan.FromSeconds(3),
    };

    public static async Task<HttpProbeResult> ProbeAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            string? body = null;
            if (response.Content is not null)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var ms = new MemoryStream();
                var buffer = new byte[8192];
                int total = 0;
                int read;
                while (total < MaxBodyBytes &&
                       (read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    total += read;
                }
                body = Encoding.UTF8.GetString(ms.ToArray());
            }
            return new HttpProbeResult { HttpOk = true, Body = body };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HttpProbeResult { HttpOk = false, Error = ex.Message };
        }
    }
}
