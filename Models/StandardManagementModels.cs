namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 规范管理目录节点。
/// </summary>
public sealed class StandardManagementCategoryDto
{
    public long Id { get; init; }
    public long? ParentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

/// <summary>
/// 规范系列及其基本元数据。
/// </summary>
public sealed class StandardManagementSeriesDto
{
    public long Id { get; init; }
    public long? CategoryId { get; init; }
    public string SeriesCode { get; init; } = string.Empty;
    public string SeriesName { get; init; } = string.Empty;
    public string StandardNumber { get; init; } = string.Empty;
    public string TableNumber { get; init; } = string.Empty;
    public string PressureRating { get; init; } = string.Empty;
    public string FlangeType { get; init; } = string.Empty;
    public string FaceType { get; init; } = string.Empty;
}

/// <summary>
/// 规范文件版本。
/// </summary>
public sealed class StandardDocumentVersionDto
{
    public long Id { get; init; }
    public long SeriesId { get; init; }
    public string VersionNo { get; init; } = string.Empty;
    public string VersionLabel { get; init; } = string.Empty;
    public string ChangeSummary { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public DateTime? CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>
/// 规范版本下的文件或附件。
/// </summary>
public sealed class StandardDocumentFileDto
{
    public long Id { get; init; }
    public long VersionId { get; init; }
    public string FileRole { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
}

/// <summary>
/// 规范管理目录查询结果。
/// </summary>
public sealed class StandardManagementTreeResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<StandardManagementCategoryDto> Categories { get; init; } = Array.Empty<StandardManagementCategoryDto>();
    public IReadOnlyList<StandardManagementSeriesDto> Series { get; init; } = Array.Empty<StandardManagementSeriesDto>();
}

/// <summary>
/// 规范搜索结果行，包含系列和当前版本摘要。
/// </summary>
public sealed class StandardManagementSearchItemDto
{
    public StandardManagementSeriesDto Series { get; init; } = new();
    public StandardDocumentVersionDto? CurrentVersion { get; init; }
    public int VersionCount { get; init; }
}

/// <summary>
/// 规范搜索分页结果。
/// </summary>
public sealed class StandardManagementSearchResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<StandardManagementSearchItemDto> Items { get; init; } = Array.Empty<StandardManagementSearchItemDto>();
}
