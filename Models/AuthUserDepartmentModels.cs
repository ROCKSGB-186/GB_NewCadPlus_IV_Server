namespace GB_NewCadPlus_IV.UploadApi.Models;

public sealed class LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    // 登录账号，用于定位需要修改密码的用户。
    public string Username { get; init; } = string.Empty;
    // 注册时登记的手机号码，用于身份核验。
    public string Phone { get; init; } = string.Empty;
    // 注册时登记的邮箱，用于身份核验。
    public string Email { get; init; } = string.Empty;
    // 修改后的新密码。
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class LoginResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public UserDto? User { get; init; }
}

public sealed class RegisterUserRequest
{
    // 登录账号。
    public string Username { get; init; } = string.Empty;
    // 登录密码，服务器只保存哈希值，不保存明文。
    public string Password { get; init; } = string.Empty;
    // 注册用户所属部门。
    public int DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public string? RealName { get; init; }
    public string? Gender { get; init; }
    // 手机号，用于找回密码身份校验。
    public string? Phone { get; init; }
    // 邮箱，用于找回密码身份校验。
    public string? Email { get; init; }
    public string? Role { get; init; }
}

public class UserDto
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string RealName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public sealed class UserMutationRequest
{
    public string Username { get; init; } = string.Empty;
    public string? Password { get; init; }
    public int? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public string? RealName { get; init; }
    public string? Gender { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Role { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class DepartmentMutationRequest
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public int? CadCategoryId { get; init; }
    public int SortOrder { get; init; }
    public int? ManagerUserId { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class MutationResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Id { get; init; }
    public IReadOnlyList<DepartmentDto> Departments { get; init; } = Array.Empty<DepartmentDto>();
}
