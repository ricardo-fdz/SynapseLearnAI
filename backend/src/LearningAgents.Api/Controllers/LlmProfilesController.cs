using LearningAgents.Infrastructure.LLM;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/llm-profiles")]
public sealed class LlmProfilesController(IOptions<LlmProfilesOptions> options) : ControllerBase
{
    [HttpGet]
    public ActionResult<object> GetAll()
    {
        var value = options.Value;
        return Ok(new
        {
            defaultProfile = value.DefaultProfile,
            profiles = value.Profiles
                .OrderBy(profile => profile.Key)
                .Select(profile => new
                {
                    name = profile.Key,
                    provider = profile.Value.Provider,
                    model = profile.Value.Model,
                    fallbackProfiles = profile.Value.FallbackProfiles
                })
        });
    }
}
