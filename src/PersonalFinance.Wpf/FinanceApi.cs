using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalFinance.Wpf;

public sealed class FinanceApi : IDisposable
{
    private readonly HttpClient client;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public FinanceApi()
    {
        var address = Environment.GetEnvironmentVariable("PERSONAL_FINANCE_API_URL") ?? "http://localhost:5080";
        client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true })
        { BaseAddress = new Uri(address.TrimEnd('/') + "/api/v1/"), Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<T> GetAsync<T>(string path)
    {
        using var response = await client.GetAsync(path);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(json))!;
    }

    public async Task<T> SendAsync<T>(string path, object? data = null, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path)
        {
            Content = data is null ? null : JsonContent.Create(data, options: json)
        };
        using var response = await client.SendAsync(request);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<T>(json))!;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("title", out var title)) throw new InvalidOperationException(title.GetString());
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"Ошибка API: {(int)response.StatusCode}.");
    }

    public void Dispose() => client.Dispose();
}
