namespace BlooLynx;

/// <summary>Outcome of an API call, replacing exceptions/magic strings for expected HTTP failures.</summary>
public class Response
{
    public bool IsSuccessful { get; }
    public int ResponseCode { get; }
    public string? ErrorMessage { get; }

    /// <summary>
    /// The command's tracking id, from whichever of the "tmsTid"/"transactionId"/"Xid" response headers was
    /// present. Only remote vehicle commands carry this. Pass this response to
    /// <see cref="Vehicle.WaitForCommandAsync"/> to poll for real completion instead of trusting the HTTP 200.
    /// </summary>
    public string? TransactionId { get; }

    /// <summary>The service_type value <see cref="Vehicle.WaitForCommandAsync"/> should poll with for this command.</summary>
    internal string? ServiceType { get; }

    protected Response(bool isSuccessful, int responseCode, string? errorMessage, string? transactionId = null, string? serviceType = null)
    {
        IsSuccessful = isSuccessful;
        ResponseCode = responseCode;
        ErrorMessage = errorMessage;
        TransactionId = transactionId;
        ServiceType = serviceType;
    }

    public static Response Success(int responseCode = 200, string? transactionId = null, string? serviceType = null) =>
        new(true, responseCode, null, transactionId, serviceType);

    public static Response Failure(int responseCode, string errorMessage) => new(false, responseCode, errorMessage);
}

/// <summary>Outcome of an API call that returns data on success.</summary>
public sealed class Response<T> : Response
{
    public T? Data { get; }

    private Response(bool isSuccessful, int responseCode, string? errorMessage, T? data)
        : base(isSuccessful, responseCode, errorMessage)
    {
        Data = data;
    }

    public static Response<T> Success(T data, int responseCode = 200) => new(true, responseCode, null, data);

    public static new Response<T> Failure(int responseCode, string errorMessage) => new(false, responseCode, errorMessage, default);
}
