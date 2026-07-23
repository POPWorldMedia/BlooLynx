using System.Linq;
using System.Text.Json;

namespace BlooLynx;

/// <summary>Builds a <see cref="Response"/>/<see cref="Response{T}"/> from an HTTP result. Single place that
/// decides what counts as success and how a failure body becomes an error message.</summary>
internal static class ResponseFactory
{
    private static readonly string[] TransactionIdHeaders = { "tmsTid", "transactionId", "Xid" };

    public static async Task<Response> FromHttpResponseAsync(HttpResponseMessage response, string? serviceType = null)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ExtractErrorMessage(body, response.Content.Headers.ContentType?.MediaType);
            return Response.Failure((int)response.StatusCode, errorMessage);
        }

        var transactionId = GetFirstTransactionIdHeaderValue(response);
        return Response.Success((int)response.StatusCode, transactionId, serviceType);
    }

    public static async Task<Response<T>> FromHttpResponseAsync<T>(HttpResponseMessage response, Func<string, T> parse)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ExtractErrorMessage(body, response.Content.Headers.ContentType?.MediaType);
            return Response<T>.Failure((int)response.StatusCode, errorMessage);
        }

        var data = parse(body);
        return Response<T>.Success(data, (int)response.StatusCode);
    }

    private static string? GetFirstTransactionIdHeaderValue(HttpResponseMessage response)
    {
        foreach (var header in TransactionIdHeaders)
        {
            if (response.Headers.TryGetValues(header, out var values))
            {
                var value = values.FirstOrDefault();
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>BlueLink error bodies are JSON with an "errorMessage" property. Only attempts to parse when the
    /// response actually declares a JSON content type, so a plain-text/HTML error body falls back to the raw body
    /// instead of triggering a parse failure; a JSON-declared body that fails to parse is a genuine, unexpected
    /// problem and is allowed to throw rather than being silently swallowed.</summary>
    private static string ExtractErrorMessage(string body, string? mediaType)
    {
        if (mediaType != "application/json")
        {
            return body;
        }

        using var document = JsonDocument.Parse(body);
        var hasErrorMessage = document.RootElement.TryGetProperty("errorMessage", out var errorMessageElement);
        if (hasErrorMessage && errorMessageElement.ValueKind == JsonValueKind.String)
        {
            var errorMessage = errorMessageElement.GetString();
            return errorMessage!;
        }

        return body;
    }
}
