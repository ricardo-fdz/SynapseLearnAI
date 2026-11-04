using LearningAgents.Application.Dtos.LlmProfiles;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/llm-profiles")]
public sealed class LlmProfilesController(ILlmProfileCatalog catalog) : ControllerBase
{
    [HttpGet]
    public ActionResult<LlmProfilesCatalogResponse> GetAll() => Ok(catalog.GetCatalog());
}
