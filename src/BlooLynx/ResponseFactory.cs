using System.Linq;

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
            return Response.Failure((int)response.StatusCode, body);
        }

        var transactionId = TransactionIdHeaders
            .Select(header => response.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null)
            .FirstOrDefault(value => value is not null);

        return Response.Success((int)response.StatusCode, transactionId, serviceType);
    }

    public static async Task<Response<T>> FromHttpResponseAsync<T>(HttpResponseMessage response, Func<string, T> parse)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Response<T>.Failure((int)response.StatusCode, body);
        }

        return Response<T>.Success(parse(body), (int)response.StatusCode);
    }
}
