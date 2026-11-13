using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Management;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/management")]
[Authorize(Policy = ManagementRequirement.PolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class ManagementDashboardController(GetManagementDashboardDatasetUseCase dataset) : ControllerBase
{
    [HttpGet("dashboard-dataset")]
    [ProducesResponseType<ManagementDashboardDatasetResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagementDashboardDatasetResponse>> Dataset(CancellationToken cancellationToken) =>
        Ok(await dataset.ExecuteAsync(cancellationToken));
}
