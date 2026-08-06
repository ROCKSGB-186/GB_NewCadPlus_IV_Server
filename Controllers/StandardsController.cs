using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

/// <summary>
/// 规范库查询接口。
/// 规范数据由服务器统一访问，客户端不直接连接数据库。
/// </summary>
[ApiController]
[Route("api/standards")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class StandardsController : ControllerBase
{
    private readonly StandardQueryService _standardQueryService;
    private readonly StandardImportService _standardImportService;
    private readonly ILogger<StandardsController> _logger;

    /// <summary>
    /// 创建规范接口控制器。
    /// </summary>
    public StandardsController(
        StandardQueryService standardQueryService,
        StandardImportService standardImportService,
        ILogger<StandardsController> logger)
    {
        _standardQueryService = standardQueryService ?? throw new ArgumentNullException(nameof(standardQueryService));
        _standardImportService = standardImportService ?? throw new ArgumentNullException(nameof(standardImportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据法兰系列、DN、PN 等条件查询规范参数。
    /// 请求：POST /api/standards/flanges/match
    /// </summary>
    [HttpPost("flanges/match")]
    [ProducesResponseType(typeof(StandardMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<StandardMatchResponse>> MatchFlangeAsync(
        [FromBody] StandardMatchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            StandardMatchResponse response = await _standardQueryService
                .MatchFlangeAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "法兰规范查询参数无效。DN={DN}, PN={PN}", request?.DN, request?.PN);
            return BadRequest(new StandardMatchResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "法兰规范查询接口执行失败。DN={DN}, PN={PN}", request?.DN, request?.PN);
            return StatusCode(StatusCodes.Status500InternalServerError, new StandardMatchResponse
            {
                Success = false,
                Message = "法兰规范查询失败，请查看服务器日志。"
            });
        }
    }

    /// <summary>
    /// 上传规范 JSON 并预览校验结果，不写入数据库。
    /// 请求：POST /api/standards/import/preview-json
    /// </summary>
    [HttpPost("import/preview-json")]
    [ProducesResponseType(typeof(StandardImportPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StandardImportPreviewResponse>> PreviewJsonImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "请上传非空 JSON 文件。" });
        }

        try
        {
            await using Stream stream = file.OpenReadStream();
            StandardImportPreviewResponse response = await _standardImportService
                .PreviewJsonAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            return Ok(response);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "规范 JSON 预览失败。FileName={FileName}", file.FileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "规范 JSON 预览失败，请查看服务器日志。" });
        }
    }

    /// <summary>
    /// 上传规范 Excel 并预览校验结果，不写入数据库。
    /// 请求：POST /api/standards/import/preview
    /// </summary>
    [HttpPost("import/preview")]
    [ProducesResponseType(typeof(StandardImportPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StandardImportPreviewResponse>> PreviewImportAsync(
        IFormFile file,
        [FromForm] string familyCode,
        [FromForm] string familyName,
        [FromForm] string seriesCode,
        [FromForm] string seriesName,
        [FromForm] string standardNumber,
        [FromForm] string tableNumber,
        [FromForm] string pressureRating,
        [FromForm] string? flangeType,
        [FromForm] string? faceType,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "请上传非空 Excel 文件。" });
        }

        try
        {
            var series = new StandardSeriesDto
            {
                FamilyCode = familyCode?.Trim() ?? string.Empty,
                FamilyName = familyName?.Trim() ?? string.Empty,
                SeriesCode = seriesCode?.Trim() ?? string.Empty,
                SeriesName = seriesName?.Trim() ?? string.Empty,
                StandardNumber = standardNumber?.Trim() ?? string.Empty,
                TableNumber = tableNumber?.Trim() ?? string.Empty,
                PressureRating = pressureRating?.Trim() ?? string.Empty,
                FlangeType = string.IsNullOrWhiteSpace(flangeType) ? "PL" : flangeType.Trim(),
                FaceType = string.IsNullOrWhiteSpace(faceType) ? "RF" : faceType.Trim()
            };

            await using Stream stream = file.OpenReadStream();
            StandardImportPreviewResponse response = await _standardImportService
                .PreviewAsync(stream, series, cancellationToken)
                .ConfigureAwait(false);
            return Ok(response);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "规范 Excel 预览失败。FileName={FileName}", file.FileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "规范 Excel 预览失败，请查看服务器日志。" });
        }
    }

    /// <summary>
    /// 确认 Excel 预览批次并导入数据库。
    /// 请求：POST /api/standards/import/commit?batchId=...&allowWarnings=false
    /// </summary>
    [HttpPost("import/commit")]
    [ProducesResponseType(typeof(StandardImportCommitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StandardImportCommitResponse>> CommitImportAsync(
        [FromQuery] string batchId,
        [FromQuery] bool allowWarnings,
        CancellationToken cancellationToken)
    {
        try
        {
            StandardImportCommitResponse response = await _standardImportService
                .CommitAsync(batchId, allowWarnings, cancellationToken)
                .ConfigureAwait(false);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "规范 Excel 确认导入失败。BatchId={BatchId}", batchId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "规范数据导入失败，请查看服务器日志。" });
    }
    }
}
