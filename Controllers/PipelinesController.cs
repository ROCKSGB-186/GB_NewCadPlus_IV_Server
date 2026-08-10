using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

/// <summary>
/// 管道通用参数接口。
/// 进口和出口共用同一套参数，角色只影响图面样式。
/// </summary>
[ApiController]
[Route("api/pipelines")]
public sealed class PipelinesController : ControllerBase
{
    private readonly PipelineCatalogService _pipelineCatalogService;
    private readonly PipelineDesignStandardService _pipelineDesignStandardService;
    private readonly ILogger<PipelinesController> _logger;

    /// <summary>
    /// 创建管道接口控制器。
    /// </summary>
    public PipelinesController(
        PipelineCatalogService pipelineCatalogService,
        PipelineDesignStandardService pipelineDesignStandardService,
        ILogger<PipelinesController> logger)
    {
        _pipelineCatalogService = pipelineCatalogService
            ?? throw new ArgumentNullException(nameof(pipelineCatalogService));
        _pipelineDesignStandardService = pipelineDesignStandardService
            ?? throw new ArgumentNullException(nameof(pipelineDesignStandardService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取管道通用字段和进口/出口图面样式。
    /// </summary>
    [HttpGet("fields")]
    [ProducesResponseType(typeof(PipelineFieldCatalogResponse), StatusCodes.Status200OK)]
    public ActionResult<PipelineFieldCatalogResponse> GetFields()
    {
        _logger.LogInformation("收到管道字段目录请求。Route={Route}", "GET /api/pipelines/fields");
        return Ok(_pipelineCatalogService.GetFieldCatalog());
    }

    /// <summary>
    /// 获取管道通用参数默认值。
    /// </summary>
    [HttpGet("defaults")]
    [ProducesResponseType(typeof(PipelineDefaultsResponse), StatusCodes.Status200OK)]
    public ActionResult<PipelineDefaultsResponse> GetDefaults()
    {
        _logger.LogInformation("收到管道默认值请求。Route={Route}", "GET /api/pipelines/defaults");
        return Ok(_pipelineCatalogService.GetDefaults());
    }

    /// <summary>
    /// 根据管道基础属性匹配 GB 设计规范。
    /// </summary>
    [HttpPost("design-standard/match")]
    [ProducesResponseType(typeof(PipelineDesignStandardMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<PipelineDesignStandardMatchResponse> MatchDesignStandard(
        [FromBody] PipelineDesignStandardMatchRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new { success = false, message = "管道 GB 设计规范匹配请求不能为空。" });
            }

            _logger.LogInformation(
                "收到管道 GB 设计规范匹配请求：StandardNo={StandardNo}, DN={DN}, PN={PN}",
                request.DrawingStandardNo,
                request.DN,
                request.PN);

            return Ok(_pipelineDesignStandardService.Match(request));
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "管道 GB 设计规范请求参数无效。");
            return BadRequest(new { success = false, message = exception.Message });
        }
    }
}
