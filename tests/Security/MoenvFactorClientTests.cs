using System.Net;
using System.Text;
using CarbonFootprint.Web.Services;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Security.Tests;

public sealed class MoenvFactorClientTests
{
    [Fact]
    public async Task DownloadAsync_UsesStableNewestFirstPagingAndKeepsOneLatestRecordPerFactor()
    {
        var handler = new RecordingHandler(
            """
            {
              "records": [
                { "name": "電力", "coe": "0.5", "unit": "kWh", "departmentname": "環境部", "announcementyear": "2026" },
                { "name": "電力", "coe": "0.4", "unit": "kWh", "departmentname": "環境部", "announcementyear": "2025" }
              ]
            }
            """);
        var client = new MoenvFactorClient(
            new HttpClient(handler),
            Options.Create(new MoenvFactorSourceOptions { ApiKey = "not-a-real-key" }));

        var result = await client.DownloadAsync(CancellationToken.None);

        var factor = Assert.Single(result.Records);
        Assert.Equal(0.5m, factor.Value);
        Assert.Contains("sort=ImportDate%20desc", handler.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", factor.SourceRecordSha256);
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
