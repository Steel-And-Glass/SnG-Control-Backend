using Microsoft.Extensions.Options;
using SnG.Control.Application.Identity;

namespace SnG.Control.Api.Authorization;

public sealed class ConfiguredCommercialHeadIdentity(IOptions<CommercialHeadOptions> options) : ICommercialHeadIdentity
{
    public string Subject => options.Value.CommercialHeadSubject;
}
