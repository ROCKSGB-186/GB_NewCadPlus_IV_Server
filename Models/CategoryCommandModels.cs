namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 新增主分类请求对象。
/// </summary>
public sealed class AddCategoryRequest
{
    /// <summary>
    /// 分类名称，必填。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 分类显示名称，可为空；为空时服务器使用 Name。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// 排序号，可为空或小于等于 0；服务器会自动生成最大排序号加 1。
    /// </summary>
    public int? SortOrder { get; init; }
}

/// <summary>
/// 分类新增接口统一返回对象。
/// </summary>
public sealed class CategoryMutationResponse
{
    /// <summary>
    /// 操作是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 操作提示信息。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 新增后的主分类数据。
    /// </summary>
    public CategoryDto? Category { get; init; }

}

/// <summary>
/// 新增子分类请求对象。
/// </summary>
public sealed class AddSubcategoryRequest
{
    /// <summary>
    /// 子分类名称，必填。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 子分类显示名称，可为空；为空时服务器使用 Name。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// 排序号，可为空或小于等于 0；服务器自动生成。
    /// </summary> 
    public int? SortOrder { get; init; }
}

/// <summary>
/// 新增子分类统一返回对象。
/// </summary>
public sealed class SubcategoryMutationResponse
{
    /// <summary>
    /// 操作是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 操作提示信息。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 新增后的子分类数据。
    /// </summary>
    public SubcategoryDto? Subcategory { get; init; }
}

/// <summary>
/// 删除分类统一返回对象。
/// </summary>
public sealed class CategoryDeleteResponse
{
    /// <summary>
    /// 操作是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 操作提示信息。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 实际删除的分类 ID。
    /// </summary>
    public int DeletedId { get; init; }
}

/// <summary>
/// 更新分类请求对象。
/// </summary>
public sealed class UpdateCategoryRequest
{
    /// <summary>
    /// 分类名称，必填。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 分类显示名称；为空时使用分类名称。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// 分类排序号；为空或小于等于零时保留服务器当前值。
    /// </summary>
    public int? SortOrder { get; init; }
}

/// <summary>
/// 更新分类接口统一返回对象。
/// </summary>
public sealed class CategoryUpdateResponse
{
    /// <summary>
    /// 操作是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 操作提示信息。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 更新后的分类 ID。
    /// </summary>
    public int UpdatedId { get; init; }

    /// <summary>
    /// 更新后的分类名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 更新后的显示名称。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 更新后的排序号。
    /// </summary>
    public int SortOrder { get; init; }
}
