namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 新建或修改规范主分类/子分类的请求参数。
/// ParentId 为空表示主分类，否则表示挂接到指定主分类下的子分类。
/// </summary>
public sealed class StandardCategoryCommandRequest
{
    public long? ParentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// 修改规范版本显示名称的请求参数。
/// </summary>
public sealed class StandardVersionRenameRequest
{
    public string Name { get; init; } = string.Empty;
}

public sealed class StandardImportCommitRequest
{
    /// <summary>预览接口返回的临时批次标识。</summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>用户是否已核对并接受预览警告。</summary>
    public bool AllowWarnings { get; init; }

    /// <summary>用户在预览页面最终确认的规范元数据。</summary>
    public StandardSeriesDto Series { get; init; } = new();

    /// <summary>预览命中已有规范时的用户处理意图。</summary>
    public StandardImportDuplicateStrategy DuplicateStrategy { get; init; } = StandardImportDuplicateStrategy.NewImport;
}

/// <summary>
/// 同一规范已存在时的导入处理意图。
/// </summary>
public enum StandardImportDuplicateStrategy
{
    /// <summary>仅允许导入未命中已有规范的新规范。</summary>
    NewImport = 0,

    /// <summary>请求创建新版本；需完成法兰记录版本快照后才能启用。</summary>
    CreateVersion = 1,

    /// <summary>请求覆盖当前数据；需完成版本快照和恢复能力后才能启用。</summary>
    OverwriteCurrent = 2
}

/// <summary>
/// 移动规范分类时提交的新父分类。
/// ParentId 为空表示移动到根级分类。
/// </summary>
public sealed class StandardCategoryMoveRequest
{
    public long? ParentId { get; init; }
}

/// <summary>
/// 调整规范分类显示顺序的请求参数。
/// Direction=-1 表示上移，Direction=1 表示下移。
/// </summary>
public sealed class StandardCategoryReorderRequest
{
    public int Direction { get; init; }
}

/// <summary>
/// 移动旧规范系列时提交的目标分类。
/// </summary>
public sealed class StandardSeriesMoveRequest
{
    public long CategoryId { get; init; }
}

/// <summary>
/// 修改规范系列显示名称的请求参数。
/// </summary>
public sealed class StandardSeriesRenameRequest
{
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 与当前提交的名称或编码重复的分类信息。
/// </summary>
public sealed class StandardCategoryDuplicateDto
{
    public long Id { get; init; }
    public long? ParentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public string DuplicateReason { get; init; } = string.Empty;
}

/// <summary>
/// 规范分类重复冲突响应。
/// </summary>
public sealed class StandardCategoryConflictResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<StandardCategoryDuplicateDto> Duplicates { get; init; } = Array.Empty<StandardCategoryDuplicateDto>();
}

public sealed class StandardCategoryConflictException : InvalidOperationException
{
    public StandardCategoryConflictException(string message, IReadOnlyList<StandardCategoryDuplicateDto> duplicates)
        : base(message)
    {
        Duplicates = duplicates;
    }

    public IReadOnlyList<StandardCategoryDuplicateDto> Duplicates { get; }
}

/// <summary>
/// 新建规范版本的请求参数。
/// </summary>
public sealed class StandardVersionCreateRequest
{
    public long SeriesId { get; init; }
    public string VersionNo { get; init; } = string.Empty;
    public string? VersionLabel { get; init; }
    public string? ChangeSummary { get; init; }
    public string SourceType { get; init; } = "DOCUMENT";
    public string OperatorName { get; init; } = string.Empty;
}

public sealed class StandardDocumentFileManagementDto
{
    public long Id { get; init; }
    public long VersionId { get; init; }
    public string FileRole { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// 规范附件上传结果。
/// </summary>
public sealed class StandardFileUploadResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public long VersionId { get; init; }
    public long FileId { get; init; }
    public string FileName { get; init; } = string.Empty;
}

/// <summary>
/// 规范附件下载结果。
/// </summary>
public sealed class StandardFileDownloadResult
{
    public Stream Content { get; init; } = Stream.Null;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
}

/// <summary>
/// 规范管理操作结果。
/// </summary>
public sealed class StandardManagementOperationResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public long Id { get; init; }
}
