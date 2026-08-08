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
    private readonly StandardManagementQueryService _standardManagementQueryService;
    private readonly StandardManagementCommandService _standardManagementCommandService;
    private readonly ILogger<StandardsController> _logger;
    private readonly StandardImportService _standardImportService;

    /// <summary>
    /// 创建规范接口控制器。
    /// </summary>
    public StandardsController(
        StandardQueryService standardQueryService,
        StandardImportService standardImportService,
        StandardManagementQueryService standardManagementQueryService,
        StandardManagementCommandService standardManagementCommandService,
        ILogger<StandardsController> logger)
    {
        _standardQueryService = standardQueryService ?? throw new ArgumentNullException(nameof(standardQueryService));
        _standardManagementQueryService = standardManagementQueryService ?? throw new ArgumentNullException(nameof(standardManagementQueryService));
        _standardManagementCommandService = standardManagementCommandService ?? throw new ArgumentNullException(nameof(standardManagementCommandService));
        _standardImportService = standardImportService ?? throw new ArgumentNullException(nameof(standardImportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("management/series/{seriesId:long}/move")]
    public async Task<ActionResult<StandardManagementOperationResponse>> MoveManagementSeriesAsync(
        long seriesId,
        [FromBody] StandardSeriesMoveRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });
        try
        {
            await _standardManagementCommandService.MoveSeriesAsync(
                seriesId, request?.CategoryId ?? 0, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "旧规范已移动到目标规范库。", Id = seriesId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpPost("management/categories/{categoryId:long}/move")]
    public async Task<ActionResult<StandardManagementOperationResponse>> MoveManagementCategoryAsync(
        long categoryId,
        [FromBody] StandardCategoryMoveRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });
        try
        {
            await _standardManagementCommandService.MoveCategoryAsync(
                categoryId, request?.ParentId, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范分类移动成功。", Id = categoryId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (StandardCategoryConflictException ex)
        {
            return Conflict(new StandardCategoryConflictResponse { Success = false, Message = ex.Message, Duplicates = ex.Duplicates });
        }
        catch (InvalidOperationException ex) { return Conflict(new { success = false, message = ex.Message }); }
    }

    [HttpPost("management/categories")]
    public async Task<ActionResult<StandardManagementOperationResponse>> CreateManagementCategoryAsync(
        [FromBody] StandardCategoryCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });
        try
        {
            long id = await _standardManagementCommandService.CreateCategoryAsync(request, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范分类创建成功。", Id = id });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (StandardCategoryConflictException ex)
        {
            return Conflict(new StandardCategoryConflictResponse { Success = false, Message = ex.Message, Duplicates = ex.Duplicates });
        }
    }

    [HttpPut("management/categories/{categoryId:long}")]
    public async Task<ActionResult<StandardManagementOperationResponse>> UpdateManagementCategoryAsync(
        long categoryId,
        [FromBody] StandardCategoryCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });
        try
        {
            await _standardManagementCommandService.UpdateCategoryAsync(categoryId, request, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范分类修改成功。", Id = categoryId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (StandardCategoryConflictException ex)
        {
            return Conflict(new StandardCategoryConflictResponse { Success = false, Message = ex.Message, Duplicates = ex.Duplicates });
        }
        catch (InvalidOperationException ex) { return Conflict(new { success = false, message = ex.Message }); }
    }

    [HttpDelete("management/categories/{categoryId:long}")]
    public async Task<ActionResult<StandardManagementOperationResponse>> DeleteManagementCategoryAsync(
        long categoryId,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });
        try
        {
            await _standardManagementCommandService.DeleteCategoryAsync(categoryId, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范分类已删除。", Id = categoryId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { success = false, message = ex.Message }); }
    }

    [HttpGet("management/versions/{versionId:long}/files")]
    public async Task<ActionResult<IReadOnlyList<StandardDocumentFileManagementDto>>> GetManagementFilesAsync(
        long versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _standardManagementCommandService.GetFilesAsync(versionId, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 新建规范版本。当前阶段通过 X-Operator-Name 使用临时管理员识别。
    /// </summary>
    [HttpPost("management/versions")]
    public async Task<ActionResult<StandardManagementOperationResponse>> CreateManagementVersionAsync(
        [FromBody] StandardVersionCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });

        try
        {
            long id = await _standardManagementCommandService.CreateVersionAsync(
                new StandardVersionCreateRequest
                {
                    SeriesId = request.SeriesId,
                    VersionNo = request.VersionNo,
                    VersionLabel = request.VersionLabel,
                    ChangeSummary = request.ChangeSummary,
                    SourceType = request.SourceType,
                    OperatorName = operatorName
                }, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范版本创建成功。", Id = id });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    /// <summary>
    /// 下载规范附件。下载不要求管理员身份，是否可见由附件的有效状态决定。
    /// </summary>
    [HttpGet("management/files/{fileId:long}/download")]
    public async Task<IActionResult> DownloadManagementFileAsync(
        long fileId,
        CancellationToken cancellationToken)
    {
        try
        {
            StandardFileDownloadResult? result = await _standardManagementCommandService
                .OpenFileAsync(fileId, cancellationToken)
                .ConfigureAwait(false);
            if (result == null)
                return NotFound(new { success = false, message = "规范附件不存在或已删除。" });

            return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 上传规范版本附件。
    /// </summary>
    [HttpPost("management/versions/{versionId:long}/files")]
    [RequestSizeLimit(512L * 1024L * 1024L)]
    public async Task<ActionResult<StandardFileUploadResponse>> UploadManagementFileAsync(
        long versionId,
        IFormFile file,
        [FromForm] string? fileRole,
        [FromForm] string? description,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });

        try
        {
            StandardFileUploadResponse response = await _standardManagementCommandService.UploadFileAsync(
                versionId, file, fileRole, description, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(response);
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (InvalidDataException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    /// <summary>
    /// 软删除规范版本。
    /// </summary>
    [HttpDelete("management/versions/{versionId:long}")]
    public async Task<ActionResult<StandardManagementOperationResponse>> DeleteManagementVersionAsync(
        long versionId,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });

        try
        {
            await _standardManagementCommandService.SoftDeleteVersionAsync(versionId, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范版本已软删除。", Id = versionId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    /// <summary>
    /// 恢复历史规范版本。
    /// </summary>
    [HttpPost("management/versions/{versionId:long}/restore")]
    public async Task<ActionResult<StandardManagementOperationResponse>> RestoreManagementVersionAsync(
        long versionId,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });

        try
        {
            await _standardManagementCommandService.RestoreVersionAsync(versionId, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "历史规范版本恢复成功。", Id = versionId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    /// <summary>
    /// 查询规范管理目录树。
    /// 请求：GET /api/standards/management/tree
    /// </summary>
    [HttpGet("management/tree")]
    [ProducesResponseType(typeof(StandardManagementTreeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StandardManagementTreeResponse>> GetManagementTreeAsync(
        CancellationToken cancellationToken)
    {
        StandardManagementTreeResponse response = await _standardManagementQueryService
            .GetTreeAsync(cancellationToken)
            .ConfigureAwait(false);
        return Ok(response);
    }

    /// <summary>
    /// 按专业/类别、标准号、系列编码或关键词分页查询规范系列。
    /// 请求：GET /api/standards/management/search?keyword=法兰&page=1&pageSize=50
    /// </summary>
    [HttpGet("management/search")]
    [ProducesResponseType(typeof(StandardManagementSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StandardManagementSearchResponse>> SearchManagementAsync(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        StandardManagementSearchResponse response = await _standardManagementQueryService
            .SearchAsync(keyword, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Ok(response);
    }

    /// <summary>
    /// 查询指定规范系列的历史版本。
    /// 请求：GET /api/standards/management/series/{seriesId}/versions
    /// </summary>
    [HttpGet("management/series/{seriesId:long}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<StandardDocumentVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<StandardDocumentVersionDto>>> GetManagementVersionsAsync(
        long seriesId,
        CancellationToken cancellationToken)
    {
        if (seriesId <= 0)
        {
            return BadRequest(new { success = false, message = "规范系列 ID 必须大于 0。" });
        }

        IReadOnlyList<StandardDocumentVersionDto> versions = await _standardManagementQueryService
            .GetVersionsAsync(seriesId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(versions);
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
