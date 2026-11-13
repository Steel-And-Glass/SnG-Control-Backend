using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Management;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/management")]
[Authorize(Policy = ManagementRequirement.PolicyName)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class ManagementController(GetManagementOverviewUseCase overview, ListManagementTasksUseCase tasks, GetManagementQuotationPortfolioUseCase portfolio) : ControllerBase
{
    [HttpGet("overview")]
    [ProducesResponseType<ManagementOverviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagementOverviewResponse>> Overview(CancellationToken cancellationToken) =>
        Ok(await overview.ExecuteAsync(cancellationToken));

    [HttpGet("tasks")]
    [ProducesResponseType<IReadOnlyList<ManagementTaskResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ManagementTaskResponse>>> Tasks(CancellationToken cancellationToken) =>
        Ok(await tasks.ExecuteAsync(cancellationToken));
    [HttpGet("quotation-portfolio")]
    [ProducesResponseType<ManagementQuotationPortfolioResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagementQuotationPortfolioResponse>> QuotationPortfolio(CancellationToken cancellationToken) =>
        Ok(await portfolio.ExecuteAsync(cancellationToken));
}
