using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Api.Authorization;
using SmartAttendance.Api.Reports;
using SmartAttendance.Application.Reports;

namespace SmartAttendance.Api.Controllers;

[ApiController]
[Route("api/employees/{uid}/attendance-reports/email")]
[Authorize(Policy = AuthorizationPolicies.ManagerOrAdmin)]
public sealed class AttendanceReportEmailsController(
    IAttendanceReportEmailService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SendAttendanceReportEmailResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<SendAttendanceReportEmailResponse>> Send(
        string uid,
        SendAttendanceReportEmailRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ReportValidationException("Idempotency-Key is required.");
        }

        var result = await service.SendAsync(
                new SendAttendanceReportCommand(
                    User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                        throw new InvalidOperationException("The caller UID is missing."),
                    uid,
                    request.PeriodKind,
                    request.AnchorDate,
                    idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);
        return Accepted(SendAttendanceReportEmailResponse.FromDomain(result));
    }
}
