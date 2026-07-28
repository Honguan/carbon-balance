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
            [
                { "name": "電力", "coe": "0.5", "unit": "kWh", "departmentname": "環境部", "announcementyear": "2026" },
                { "name": "電力", "coe": "0.4", "unit": "度(kwh)", "departmentname": "環境部", "announcementyear": "2025" }
            ]
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

    [Fact]
    public async Task DownloadAsync_WithoutConfiguredKey_ResolvesOfficialPublicDownloadUrl()
    {
        var handler = new MetadataResolvingHandler();
        var client = new MoenvFactorClient(
            new HttpClient(handler),
            Options.Create(new MoenvFactorSourceOptions()));

        var result = await client.DownloadAsync(CancellationToken.None);

        Assert.Single(result.Records);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal("data.gov.tw", handler.RequestUris[0].Host);
        Assert.Equal("data.moenv.gov.tw", handler.RequestUris[1].Host);
        Assert.Contains("api_key=public-resource-key", handler.RequestUris[1].Query, StringComparison.Ordinal);
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

    private sealed class MetadataResolvingHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            RequestUris.Add(uri);
            var json = uri.Host == "data.gov.tw"
                ? """
                  {
                    "success": true,
                    "result": {
                      "distribution": [
                        {
                          "resourceFormat": "JSON",
                          "resourceDownloadUrl": "https://data.moenv.gov.tw/api/v2/cfp_p_02?api_key=public-resource-key&limit=1000&sort=ImportDate desc&format=JSON"
                        }
                      ]
                    }
                  }
                  """
                : """[{"name":"電力","coe":"0.5","unit":"度(kwh)","departmentname":"環境部","announcementyear":"2026"}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
