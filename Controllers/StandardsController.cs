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
    private readonly DynamicStandardPreviewService _dynamicStandardPreviewService;
    private readonly DynamicStandardImportService _dynamicStandardImportService;

    /// <summary>
    /// 创建规范接口控制器。
    /// </summary>
    public StandardsController(
        StandardQueryService standardQueryService,
        StandardImportService standardImportService,
        StandardManagementQueryService standardManagementQueryService,
        StandardManagementCommandService standardManagementCommandService,
        DynamicStandardPreviewService dynamicStandardPreviewService,
        DynamicStandardImportService dynamicStandardImportService,
        ILogger<StandardsController> logger)
    {
        _standardQueryService = standardQueryService ?? throw new ArgumentNullException(nameof(standardQueryService));
        _standardManagementQueryService = standardManagementQueryService ?? throw new ArgumentNullException(nameof(standardManagementQueryService));
        _standardManagementCommandService = standardManagementCommandService ?? throw new ArgumentNullException(nameof(standardManagementCommandService));
        _standardImportService = standardImportService ?? throw new ArgumentNullException(nameof(standardImportService));
        _dynamicStandardPreviewService = dynamicStandardPreviewService ?? throw new ArgumentNullException(nameof(dynamicStandardPreviewService));
        _dynamicStandardImportService = dynamicStandardImportService ?? throw new ArgumentNullException(nameof(dynamicStandardImportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPut("management/versions/{versionId:long}/name")]
    public async Task<ActionResult<StandardManagementOperationResponse>> RenameManagementVersionAsync(
        long versionId,
        [FromBody] StandardVersionRenameRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });
        try
        {
            await _standardManagementCommandService.RenameVersionAsync(versionId, request?.Name ?? string.Empty, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范细分重命名成功。", Id = versionId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpGet("dynamic/versions/{versionId:long}/content")]
    [ProducesResponseType(typeof(DynamicStandardContentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DynamicStandardContentResponse>> GetDynamicContentByVersionAsync(
        long versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            DynamicStandardContentResponse? response = await _standardQueryService
                .GetDynamicContentByVersionAsync(versionId, cancellationToken)
                .ConfigureAwait(false);
            return response == null ? NotFound(new { success = false, message = "该规范版本没有动态内容。" }) : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>确认动态规范预览并保存为导入批次，不直接发布到具体部件业务表。接口签名保持稳定，基础规范创建参数后续由扩展请求模型承载。</summary>
    [HttpPost("import/dynamic-commit")]
    [ProducesResponseType(typeof(DynamicStandardImportCommitResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DynamicStandardImportCommitResponse>> DynamicCommitImportAsync(
        [FromBody] DynamicStandardImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("动态确认请求开始。BatchId={BatchId}, SeriesId={SeriesId}, RowCount={RowCount}", request?.BatchId ?? string.Empty, request?.SeriesId ?? 0, request?.Rows?.Count ?? 0);
        if (request == null)
            return BadRequest(new { success = false, message = "动态确认请求不能为空。" });
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
        {
            _logger.LogWarning("动态确认请求被拒绝：操作者不是管理员。BatchId={BatchId}", request?.BatchId ?? string.Empty);
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以导入动态规范。" });
        }

        try
        {
            return Ok(await _dynamicStandardImportService.CommitAsync(request, operatorName, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { _logger.LogWarning(ex, "动态确认目标资源不存在。BatchId={BatchId}", request?.BatchId ?? string.Empty); return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { _logger.LogWarning(ex, "动态确认批次状态冲突。BatchId={BatchId}", request?.BatchId ?? string.Empty); return Conflict(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态确认请求发生未处理异常。BatchId={BatchId}, SeriesId={SeriesId}", request?.BatchId ?? string.Empty, request?.SeriesId ?? 0);
            string detail = HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? ex.GetBaseException().Message
                : "动态规范确认失败，请查看服务器日志。";
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = detail });
        }
    }

    /// <summary>按数据库模板预览任意 Excel 表头，不写入规范数据。</summary>
    [HttpPost("import/dynamic-preview")]
    [ProducesResponseType(typeof(DynamicStandardPreviewResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DynamicStandardPreviewResponse>> DynamicPreviewImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        _logger.LogInformation("动态预览请求开始。FileName={FileName}, FileLength={FileLength}", file?.FileName ?? string.Empty, file?.Length ?? 0);
        if (file == null || file.Length == 0) return BadRequest(new { success = false, message = "请上传非空 Excel 文件。" });
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { success = false, message = "动态预览当前只支持 .xlsx 文件。" });
        try
        {
            await using Stream stream = file.OpenReadStream();
            return Ok(await _dynamicStandardPreviewService.PreviewAsync(stream, file.FileName, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidDataException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态规范预览失败。FileName={FileName}, FileLength={FileLength}, ContentType={ContentType}", file.FileName, file.Length, file.ContentType ?? string.Empty);
            string databaseType = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Database:Type"] ?? "DM";
            string message = string.Equals(databaseType, "DM", StringComparison.OrdinalIgnoreCase)
                ? "动态规范预览失败（达梦数据库）。请检查模板表是否已在配置 Schema 中创建，并查看服务器日志中的 SQL 异常。"
                : "动态规范预览失败，请查看服务器日志。";
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message });
        }
    }

    /// <summary>查询动态规范系列当前版本的原始字段内容。</summary>
    [HttpGet("dynamic/series/{seriesId:long}/content")]
    [ProducesResponseType(typeof(DynamicStandardContentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DynamicStandardContentResponse>> GetDynamicContentAsync(
        long seriesId,
        CancellationToken cancellationToken)
    {
        try
        {
            DynamicStandardContentResponse? response = await _standardQueryService
                .GetDynamicContentAsync(seriesId, cancellationToken)
                .ConfigureAwait(false);
            return response == null ? NotFound(new { success = false, message = "该规范系列没有动态版本内容。" }) : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
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

    /// <summary>
    /// 修改规范系列名称。
    /// </summary>
    [HttpPut("management/series/{seriesId:long}/name")]
    public async Task<ActionResult<StandardManagementOperationResponse>> RenameManagementSeriesAsync(
        long seriesId,
        [FromBody] StandardSeriesRenameRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });

        try
        {
            await _standardManagementCommandService.RenameSeriesAsync(
                seriesId, request?.Name ?? string.Empty, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse
            {
                Success = true,
                Message = "规范系列重命名成功。",
                Id = seriesId
            });
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

    /// <summary>
    /// 调整规范主分类或子分类在同层级中的显示顺序。
    /// </summary>
    [HttpPost("management/categories/{categoryId:long}/reorder")]
    public async Task<ActionResult<StandardManagementOperationResponse>> ReorderManagementCategoryAsync(
        long categoryId,
        [FromBody] StandardCategoryReorderRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以管理规范库。" });

        try
        {
            await _standardManagementCommandService.ReorderCategoryAsync(
                categoryId, request?.Direction ?? 0, operatorName, cancellationToken).ConfigureAwait(false);
            return Ok(new StandardManagementOperationResponse { Success = true, Message = "规范库显示顺序调整成功。", Id = categoryId });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
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
    /// 按规范身份键精确定位规范系列及当前版本。
    /// 请求：POST /api/standards/management/identity/resolve
    /// </summary>
    [HttpPost("management/identity/resolve")]
    [ProducesResponseType(typeof(StandardIdentityResolveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StandardIdentityResolveResponse>> ResolveStandardIdentityAsync(
        [FromBody] StandardIdentityResolveRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            StandardIdentityResolveResponse response = await _standardManagementQueryService
                .ResolveIdentityAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "规范身份定位参数无效。FamilyCode={FamilyCode}, SeriesCode={SeriesCode}", request?.FamilyCode, request?.SeriesCode);
            return BadRequest(new { success = false, message = ex.Message });
        }
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
    /// 查询指定规范系列下的全部实际法兰规范记录。
    /// 请求：GET /api/standards/flanges/series/{seriesId}/records
    /// </summary>
    [HttpGet("flanges/series/{seriesId:long}/records")]
    [ProducesResponseType(typeof(IReadOnlyList<FlangeStandardRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<FlangeStandardRecordDto>>> GetFlangeRecordsAsync(
        long seriesId,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<FlangeStandardRecordDto> records = await _standardQueryService
                .GetFlangeRecordsAsync(seriesId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(records);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
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
                .PreviewJsonAsync(stream, file.FileName, cancellationToken)
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
        [FromForm] long? categoryId,
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
                CategoryId = categoryId,
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
                .PreviewAsync(stream, series, file.FileName, cancellationToken)
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
    /// 确认预览批次并导入数据库。
    /// 请求体包含用户最终确认的规范元数据和重名处理策略。
    /// </summary>
    [HttpPost("import/commit")]
    [ProducesResponseType(typeof(StandardImportCommitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StandardImportCommitResponse>> CommitImportAsync(
        [FromBody] StandardImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以导入规范。" });

        try
        {
            _logger.LogInformation(
                "收到规范导入确认请求。BatchId={BatchId}, Operator={OperatorName}, Strategy={Strategy}, SeriesName={SeriesName}, StandardNumber={StandardNumber}, TableNumber={TableNumber}, PressureRating={PressureRating}, AllowWarnings={AllowWarnings}",
                request?.BatchId,
                operatorName,
                request?.DuplicateStrategy,
                request?.Series?.SeriesName,
                request?.Series?.StandardNumber,
                request?.Series?.TableNumber,
                request?.Series?.PressureRating,
                request?.AllowWarnings);
            StandardImportCommitResponse response = await _standardImportService
                .CommitAsync(request ?? throw new ArgumentNullException(nameof(request)), operatorName, cancellationToken)
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
            _logger.LogError(ex, "规范确认导入失败。BatchId={BatchId}", request?.BatchId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "规范数据导入失败，请查看服务器日志。" });
        }
    }
}
