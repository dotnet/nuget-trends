using Microsoft.AspNetCore.Mvc;

namespace NuGetTrends.Web;

[Route("api/framework")]
[ApiController]
public class FrameworkController(
    ITfmAdoptionCache cache) : ControllerBase
{
    [HttpGet("available")]
    public async Task<ActionResult<List<TfmFamilyGroupDto>>> GetAvailableTfms(
        CancellationToken cancellationToken)
    {
        var groups = await cache.GetAvailableTfmsAsync(cancellationToken);
        return Ok(groups);
    }

    [HttpGet("adoption")]
    public async Task<ActionResult<TfmAdoptionResponse>> GetAdoption(
        [FromQuery] string? tfms = null,
        [FromQuery] string? families = null,
        CancellationToken cancellationToken = default)
    {
        var tfmList = ParseCommaSeparated(tfms);
        var familyList = ParseCommaSeparated(families);

        var response = await cache.GetAdoptionAsync(tfmList, familyList, cancellationToken);
        return Ok(response);
    }

    private static List<string>? ParseCommaSeparated(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
