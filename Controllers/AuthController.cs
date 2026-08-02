using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

[ApiController]
[Route("api/auth")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class AuthController : ControllerBase
{
    private readonly AuthUserDepartmentService _service;
    public AuthController(AuthUserDepartmentService service) => _service = service;

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => Ok(await _service.LoginAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPost("register")]
    public async Task<ActionResult<MutationResponse>> RegisterAsync([FromBody] RegisterUserRequest request, CancellationToken cancellationToken)
        => Ok(await _service.RegisterAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPost("reset-password")]
    public async Task<ActionResult<MutationResponse>> ResetPasswordAsync([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ResetPasswordAsync(request, cancellationToken).ConfigureAwait(false));
}
