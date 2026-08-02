using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

[ApiController]
[Route("api/graphics")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class GraphicsQueryController : ControllerBase
{
    private readonly GraphicQueryService _graphicQueryService;
    private readonly ILogger<GraphicsQueryController> _logger;

    public GraphicsQueryController(GraphicQueryService graphicQueryService, ILogger<GraphicsQueryController> logger)
    {
        _graphicQueryService = graphicQueryService ?? throw new ArgumentNullException(nameof(graphicQueryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("category/{categoryId:int}")]
    [ProducesResponseType(typeof(GraphicListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphicListResponse>> GetByCategoryAsync(
        int categoryId,
        [FromQuery] string categoryType = "sub",
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _graphicQueryService.GetByCategoryAsync(categoryId, categoryType, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按分类查询文件接口执行失败。CategoryId={CategoryId}, CategoryType={CategoryType}", categoryId, categoryType);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "文件查询失败，请查看服务器日志。"
            });
        }
    }
}
