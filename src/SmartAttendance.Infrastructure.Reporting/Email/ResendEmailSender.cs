using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartAttendance.Application.Reports;
using SmartAttendance.Infrastructure.Reporting.Configuration;

namespace SmartAttendance.Infrastructure.Reporting.Email;

public sealed partial class ResendEmailSender(
    HttpClient httpClient,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IReportEmailSender
{
    public async Task<ReportEmailProviderResult> SendAsync(
        ReportEmailMessage message,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled)
        {
            throw new ReportEmailUnavailableException(
                "Automatic report email is disabled.");
        }
        if (string.IsNullOrWhiteSpace(configuration.ApiKey) ||
            string.IsNullOrWhiteSpace(configuration.FromAddress))
        {
            throw new ReportEmailUnavailableException(
                "The email provider is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            configuration.ApiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", message.RequestId);
        request.Content = JsonContent.Create(new ResendRequest(
            $"{configuration.FromName} <{configuration.FromAddress}>",
            [message.Recipient],
            message.Subject,
            message.Body,
            [new ResendAttachment(
                Convert.ToBase64String(message.AttachmentBytes),
                message.AttachmentFileName)]));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogProviderRejected(logger, (int)response.StatusCode, message.RequestId);
                throw new ReportEmailUnavailableException(
                    "The email provider rejected the request.");
            }

            var result = await response.Content
                .ReadFromJsonAsync<ResendResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result?.Id))
            {
                throw new ReportEmailDeliveryUnknownException(
                    "The email provider returned an invalid success response.");
            }
            return new ReportEmailProviderResult(result.Id);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReportEmailDeliveryUnknownException(
                "The email provider timed out; delivery state is unknown.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ReportEmailDeliveryUnknownException(
                "The email provider connection failed; delivery state is unknown.",
                exception);
        }
    }

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Email provider rejected report request {RequestId} with HTTP {StatusCode}.")]
    private static partial void LogProviderRejected(
        ILogger logger,
        int statusCode,
        string requestId);

    private sealed record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("attachments")] ResendAttachment[] Attachments);

    private sealed record ResendAttachment(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("filename")] string FileName);

    private sealed record ResendResponse(
        [property: JsonPropertyName("id")] string Id);
}
