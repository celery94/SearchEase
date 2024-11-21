using Microsoft.AspNetCore.Mvc;
using SearchEase.Server.Services;

namespace SearchEase.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly SearchService _searchService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        SearchService searchService,
        ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SearchService.SearchResult>>> Search([FromQuery] string query, [FromQuery] int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("Search query cannot be empty");
        }

        try
        {
            var results = await _searchService.SearchAsync(query, maxResults);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while searching for query: {Query}", query);
            return StatusCode(500, "An error occurred while processing your search request");
        }
    }
}
