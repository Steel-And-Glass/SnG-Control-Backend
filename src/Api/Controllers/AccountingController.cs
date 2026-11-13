using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Accounting;
namespace SnG.Control.Api.Controllers;

public sealed record InitialDepositRequest([Required, MaxLength(48)] string Amount, [Required, MaxLength(40)] string ReceivedAt,
    [MaxLength(200)] string? Reference, [MaxLength(2000)] string? Note);
public sealed record AccountingHandoffRequest;
[ApiController]
[Route("api/projects/{id}")]
public sealed class AccountingController : ControllerBase
{
    [HttpGet("initial-deposit")]
    [ProducesResponseType<AccountingState>(200)]
    public async Task<IActionResult> Read(Guid id, [FromServices] AccountingService service, CancellationToken token) => await service.ReadAsync(id, token) is { } result ? Ok(result) : Failure(AccountingStatus.ProjectNotFound);
    [HttpPost("send-approved-quotation-to-accounting")]
    [Authorize(Policy = ActiveRoleRegistration.CommercialAdvisorPolicy)]
    [ProducesResponseType(409)]
    public IActionResult Send(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AccountingHandoffRequest? body, CancellationToken token) => VerificationRequired();
    [HttpPost("initial-deposit")]
    [Authorize(Policy = AccountingAssigneeRequirement.PolicyName)]
    [ProducesResponseType(409)]
    public IActionResult Record(Guid id, InitialDepositRequest body, CancellationToken token) => VerificationRequired();
    private ObjectResult VerificationRequired() => Problem(statusCode: 409, title: "Use deposit verification with receipts", extensions: new Dictionary<string, object?> { ["code"] = "DepositVerificationRequired" });
    private ObjectResult Failure(AccountingStatus status) => Problem(statusCode: status switch { AccountingStatus.Forbidden => 403, AccountingStatus.ProjectNotFound => 404, AccountingStatus.InvalidDeposit => 400, _ => 409 },
        title: "Accounting operation unavailable", extensions: new Dictionary<string, object?> { ["code"] = status.ToString() });
}
