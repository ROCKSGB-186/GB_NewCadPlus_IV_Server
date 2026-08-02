using GB_NewCadPlus_IV.UploadApi.Filters;
using GB_NewCadPlus_IV.UploadApi.Models;
using GB_NewCadPlus_IV.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_NewCadPlus_IV.UploadApi.Controllers;

[ApiController]
[Route("api/users")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class UsersController : ControllerBase
{
    private readonly AuthUserDepartmentService _service;
    public UsersController(AuthUserDepartmentService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAsync([FromQuery] int departmentId, CancellationToken cancellationToken)
        => Ok(await _service.GetUsersAsync(departmentId, cancellationToken).ConfigureAwait(false));

    [HttpPost]
    public async Task<ActionResult<MutationResponse>> AddAsync([FromBody] UserMutationRequest request, CancellationToken cancellationToken)
        => Ok(await _service.AddUserAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MutationResponse>> UpdateAsync(int id, [FromBody] UserMutationRequest request, CancellationToken cancellationToken)
        => Ok(await _service.UpdateUserAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MutationResponse>> DeleteAsync(int id, CancellationToken cancellationToken)
        => Ok(await _service.DeleteUserAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost("assign")]
    public async Task<ActionResult<MutationResponse>> AssignAsync([FromBody] UserMutationRequest request, CancellationToken cancellationToken)
        => Ok(await _service.AssignUserToDepartmentAsync(request.Username, request.DepartmentId ?? 0, cancellationToken).ConfigureAwait(false));
}
