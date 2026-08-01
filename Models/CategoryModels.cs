namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 分类树查询返回对象。
/// 客户端只依赖这些数据传输对象，不直接依赖服务器数据库模型。
/// </summary>
public sealed class CategoryTreeResponse
{
    /// <summary>
    /// 是否查询成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 返回给客户端的提示信息。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 主分类列表。
    /// </summary>
    public IReadOnlyList<CategoryDto> Categories { get; init; } = Array.Empty<CategoryDto>();

    /// <summary>
    /// 子分类列表。
    /// </summary>
    public IReadOnlyList<SubcategoryDto> Subcategories { get; init; } = Array.Empty<SubcategoryDto>();
}

/// <summary>
/// 主分类数据传输对象。
/// </summary>
public sealed class CategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string SubcategoryIds { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// 子分类数据传输对象。
/// </summary>
public sealed class SubcategoryDto
{
    public int Id { get; init; }
    public int ParentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public int Level { get; init; }
    public string SubcategoryIds { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
