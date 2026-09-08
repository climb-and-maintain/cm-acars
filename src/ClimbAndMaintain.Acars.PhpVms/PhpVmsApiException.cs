using System.Net;

namespace ClimbAndMaintain.Acars.PhpVms;

public sealed class PhpVmsApiException : HttpRequestException
{
    public PhpVmsApiException(
        HttpStatusCode statusCode,
        string message,
        TimeSpan? retryAfter = null,
        string? problemType = null,
        int? pirepState = null)
        : base(message, null, statusCode)
    {
        RetryAfter = retryAfter;
        ProblemType = problemType;
        PirepState = pirepState;
    }

    public TimeSpan? RetryAfter { get; }

    public string? ProblemType { get; }

    public int? PirepState { get; }
}
