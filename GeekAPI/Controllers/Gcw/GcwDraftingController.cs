using GeekAPI.Services.Gcw;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// Horizon B template library + tone presets for GCW drafting.
/// </summary>
[ApiController]
[Route("api/gcw/drafting")]
public class GcwDraftingController : ControllerBase
{
    [HttpGet("templates")]
    public ActionResult<IReadOnlyList<GcwDraftingCatalog.DraftTemplate>> ListTemplates() =>
        Ok(GcwDraftingCatalog.Templates);

    [HttpGet("tones")]
    public ActionResult<IReadOnlyList<GcwDraftingCatalog.TonePreset>> ListTones() =>
        Ok(GcwDraftingCatalog.Tones);
}
