namespace GB_NewCadPlus_IV.UploadApi.Models;

public sealed class DepartmentListResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<DepartmentDto> Departments { get; init; } = Array.Empty<DepartmentDto>();
}

public sealed class DepartmentDto
{
    public int Id { get; init; }
    public int? CadCategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RealName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public int? ManagerUserId { get; init; }
    public bool IsActive { get; init; }
    public int UserCount { get; init; }
}
