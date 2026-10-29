using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Api.Accounts;
using SmartAttendance.Api.Authorization;
using SmartAttendance.Application.Accounts;

namespace SmartAttendance.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(Policy = AuthorizationPolicies.ActiveAdmin)]
public sealed class AccountsController(IAccountManagementService accounts) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AccountPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountPageResponse>> List(
        [FromQuery] int pageSize = 20,
        [FromQuery] string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var page = await accounts.ListAsync(pageSize, pageToken, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new AccountPageResponse(
            page.Items.Select(AccountResponse.FromDomain).ToList(),
            page.NextPageToken));
    }

    [HttpPost]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AccountResponse>> Create(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.CreateAsync(
            GetCallerUid(),
            new CreateAccountCommand(
                request.FullName,
                request.Email,
                request.Password,
                request.Role,
                request.HourlyRate),
            cancellationToken).ConfigureAwait(false);
        return StatusCode(
            StatusCodes.Status201Created,
            AccountResponse.FromDomain(account));
    }

    [HttpPut("{uid}")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountResponse>> Update(
        string uid,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.UpdateAsync(
            GetCallerUid(),
            uid,
            new UpdateAccountCommand(request.FullName, request.HourlyRate),
            cancellationToken).ConfigureAwait(false);
        return Ok(AccountResponse.FromDomain(account));
    }

    [HttpPatch("{uid}/status")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountResponse>> ChangeStatus(
        string uid,
        ChangeAccountStatusRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.ChangeStatusAsync(
            GetCallerUid(),
            uid,
            new ChangeAccountStatusCommand(request.Status),
            cancellationToken).ConfigureAwait(false);
        return Ok(AccountResponse.FromDomain(account));
    }

    private string GetCallerUid() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException(
                "The authenticated principal has no UID claim.");
}
