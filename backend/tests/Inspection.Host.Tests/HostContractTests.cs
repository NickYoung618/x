using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Inspection.Host.Tests;

public class HostContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public HostContractTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();

    [Fact]
    public async Task Live_does_not_claim_production_readiness()
    {
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("alive", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Status_explicitly_reports_unavailable_production_capabilities()
    {
        var response = await client.GetAsync("/api/system/status");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("productionReady").GetBoolean());
        Assert.Equal("1.3", json.RootElement.GetProperty("architectureVersion").GetString());
        Assert.Equal("EngineeringFoundation", json.RootElement.GetProperty("stage").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("unavailableCapabilities").EnumerateArray());
    }

    [Fact]
    public async Task Demo_query_returns_eight_captures_without_executing_them()
    {
        var response = await client.GetAsync("/api/engineering/demo-plan");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("isTestFixture").GetBoolean());
        Assert.False(root.GetProperty("executionEnabled").GetBoolean());
        var faces = root.GetProperty("plan").GetProperty("faces").EnumerateArray().ToArray();
        Assert.Equal(2, faces.Length);
        Assert.Equal(8, faces.Sum(f => f.GetProperty("captures").GetArrayLength()));
        Assert.True(faces[1].GetProperty("requiresFlipBefore").GetBoolean());
        Assert.Equal(["A", "A", "B", "B"], faces[0].GetProperty("captures").EnumerateArray()
            .Select(c => c.GetProperty("camera").GetString()));
    }

    [Fact]
    public async Task Unimplemented_production_commands_are_not_accepted()
    {
        var response = await client.PostAsync("/api/jobs/start", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
