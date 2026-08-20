using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Dm;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

[ApiController]
[Route("api/auth")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class AuthController : ControllerBase
{
    private readonly AuthUserDepartmentService _service;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthUserDepartmentService service, ILogger<AuthController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.LoginAsync(request, cancellationToken).ConfigureAwait(false));
        }
        catch (DmException ex)
        {
            _logger.LogError(ex, "登录时连接达梦数据库失败。DatabaseType=DM");
            return StatusCode(503, new
            {
                success = false,
                message = "数据库服务暂时不可用，请稍后重试。"
            });
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<MutationResponse>> RegisterAsync([FromBody] RegisterUserRequest request, CancellationToken cancellationToken)
        => Ok(await _service.RegisterAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPost("reset-password")]
    public async Task<ActionResult<MutationResponse>> ResetPasswordAsync([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ResetPasswordAsync(request, cancellationToken).ConfigureAwait(false));
}
