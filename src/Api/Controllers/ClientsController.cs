using Microsoft.AspNetCore.Authorization;
using SnG.Control.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnG.Control.Api.Contracts;
using SnG.Control.Application.Features.Clients;
using SnG.Control.Application.Features.Clients.Create;
using SnG.Control.Application.Features.Clients.GetById;
using SnG.Control.Application.Features.Clients.List;

namespace SnG.Control.Api.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController(
    CreateClientUseCase create,
    GetClientByIdUseCase getById,
    ListClientsUseCase list) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = ActiveRoleRegistration.CommercialAdvisorPolicy)]
    [ProducesResponseType<ClientResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientResponse>> Create(
        CreateClientRequest request, CancellationToken cancellationToken)
    {
        var result = await create.ExecuteAsync(request.Name, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<ClientResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await getById.ExecuteAsync(id, cancellationToken);
        return result is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Client not found.")
            : Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await list.ExecuteAsync(cancellationToken));
}
