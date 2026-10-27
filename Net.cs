using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland
{
    /// <summary>
    /// Сначала системный прокси; если он не отвечает (VPN-клиент закрыт, а прокси в Windows остался включённым),
    /// запрос автоматически повторяется напрямую.
    /// </summary>
    class FallbackHandler : HttpMessageHandler
    {
        public static bool ForceDirect;
        static DateTime directUntil = DateTime.MinValue;
        readonly HttpMessageInvoker viaSystem = new(new SocketsHttpHandler { UseProxy = true, ConnectTimeout = TimeSpan.FromSeconds(8) });
        readonly HttpMessageInvoker direct = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10) });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            byte[] body = req.Content == null ? null : await req.Content.ReadAsByteArrayAsync(ct);
            string ctype = req.Content?.Headers.ContentType?.ToString();

            HttpRequestMessage Clone()
            {
                var r = new HttpRequestMessage(req.Method, req.RequestUri);
                foreach (var h in req.Headers) r.Headers.TryAddWithoutValidation(h.Key, h.Value);
                if (body != null)
                {
                    r.Content = new ByteArrayContent(body);
                    if (ctype != null) r.Content.Headers.TryAddWithoutValidation("Content-Type", ctype);
                }
                return r;
            }

            if (ForceDirect || DateTime.Now < directUntil) return await direct.SendAsync(Clone(), ct);
            try { return await viaSystem.SendAsync(Clone(), ct); }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                var r = await direct.SendAsync(Clone(), ct);
                directUntil = DateTime.Now.AddMinutes(2);
                return r;
            }
        }
    }
}
