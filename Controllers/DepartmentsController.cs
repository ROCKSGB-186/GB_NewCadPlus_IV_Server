using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

[ApiController]
[Route("api/departments")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class DepartmentsController : ControllerBase
{
    private readonly DepartmentQueryService _departmentQueryService;
    private readonly ILogger<DepartmentsController> _logger;

    public DepartmentsController(DepartmentQueryService departmentQueryService, ILogger<DepartmentsController> logger)
    {
        _departmentQueryService = departmentQueryService ?? throw new ArgumentNullException(nameof(departmentQueryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    [ProducesResponseType(typeof(DepartmentListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DepartmentListResponse>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _departmentQueryService.GetDepartmentsAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "部门查询接口执行失败。");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "部门查询失败，请查看服务器日志。"
            });
        }
    }
}
