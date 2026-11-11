using System.Net;
using System.Net.Http;

namespace DeltaTR.App.Diagnostics;

// Used only by --smoke-test. Requests never leave the process.
internal sealed class SmokeChatHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    public string LastBody { get; private set; } = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++;
        LastBody = await request.Content!.ReadAsStringAsync(token);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"还需要每圈油耗才能建议进站圈数。"}}]}""")
        };
    }
}
