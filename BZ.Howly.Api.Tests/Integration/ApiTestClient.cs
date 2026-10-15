using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>Small helpers shared by the integration tests.</summary>
public static class ApiTestClient
{
    public const string Password = "Correct-Horse-1";

    public static string NewUsername(string prefix = "user") => $"{prefix}.{Guid.NewGuid():N}"[..20];

    public static async Task<string> LoginInternalAsync(this HttpClient client, string username, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/internal/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    public static async Task<JsonElement> LoginCustomerAsync(this HttpClient client, string cnpj, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/customer/login", new { document = cnpj, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static Task<HttpResponseMessage> SendAsync(this HttpClient client, HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
