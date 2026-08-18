namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 规范导入模板。模板只描述如何识别和标准化数据，不保存原始文件内容。
/// </summary>
public sealed class StandardTemplateDto
{
    public long Id { get; init; }
    public string TemplateCode { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public string FamilyCode { get; init; } = string.Empty;
    public string FileType { get; init; } = "XLSX";
    public int Version { get; init; } = 1;
    public bool IsActive { get; init; } = true;
    public List<StandardTemplateColumnDto> Columns { get; init; } = new();
}

/// <summary>
/// 当前动态规范版本及其原始字段行。
/// </summary>
public sealed class DynamicStandardContentResponse
{
    public long SeriesId { get; init; }
    public long VersionId { get; init; }
    public string VersionNo { get; init; } = string.Empty;
    public string VersionLabel { get; init; } = string.Empty;
    public IReadOnlyList<DynamicStandardContentRowDto> Rows { get; init; } = Array.Empty<DynamicStandardContentRowDto>();
}

public sealed class DynamicStandardContentRowDto
{
    public int RowNumber { get; init; }
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>首次上传未匹配模板时生成的模板草稿。</summary>
public sealed class StandardTemplateDraftDto
{
    public string TemplateCode { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public string FamilyCode { get; init; } = string.Empty;
    public string FileType { get; init; } = "XLSX";
    public IReadOnlyList<StandardTemplateDraftColumnDto> Columns { get; init; } = Array.Empty<StandardTemplateDraftColumnDto>();
}

/// <summary>模板草稿字段。默认 TEXT、非必填，由管理员确认后保存。</summary>
public sealed class StandardTemplateDraftColumnDto
{
    public string Header { get; init; } = string.Empty;
    public string FieldCode { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string DataType { get; init; } = "TEXT";
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>动态规范字段和内容差异摘要。</summary>
public sealed class DynamicStandardDifferenceDto
{
    public IReadOnlyList<string> AddedHeaders { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemovedHeaders { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChangedHeaders { get; init; } = Array.Empty<string>();
    public int AddedRows { get; init; }
    public int RemovedRows { get; init; }
    public int ChangedRows { get; init; }
    public IReadOnlyList<string> ConflictRows { get; init; } = Array.Empty<string>();
}

/// <summary>动态规范更新策略。</summary>
public static class DynamicStandardUpdateStrategies
{
    public const string Replace = "REPLACE";
    public const string Merge = "MERGE";
}

/// <summary>
/// 动态规范预览确认请求。动态数据只进入导入批次，不直接写入具体部件业务表。
/// </summary>
public sealed class DynamicStandardImportCommitRequest
{
    public string BatchId { get; init; } = string.Empty;
    public long SeriesId { get; init; }
    /// <summary>已有基础规范号记录的 ID；为 0 时按基础规范号查找或创建。</summary>
    public long? StandardDocumentId { get; init; }
    /// <summary>基础规范所属目录；仅在服务器创建基础系列时使用。</summary>
    public long? CategoryId { get; init; }
    /// <summary>基础规范号，例如 GB/T 9124.1-2019。</summary>
    public string BaseStandardNumber { get; init; } = string.Empty;
    /// <summary>基础规范说明名称，可为空。</summary>
    public string BaseStandardName { get; init; } = string.Empty;
    /// <summary>当前细分规范名称，例如板式平焊钢制管法兰。</summary>
    public string SeriesName { get; init; } = string.Empty;
    /// <summary>兼容旧客户端提交的规范号；新客户端应使用 BaseStandardNumber。</summary>
    public string StandardNumber { get; init; } = string.Empty;
    /// <summary>当前细分规范系列编码，例如 PLATE_WELD。</summary>
    public string SeriesCode { get; init; } = string.Empty;
    /// <summary>当前细分规范表号。</summary>
    public string TableNumber { get; init; } = string.Empty;
    /// <summary>当前细分规范压力等级或型号。</summary>
    public string PressureRating { get; init; } = string.Empty;
    public long? VersionId { get; init; }
    public long? TemplateId { get; init; }
    public string FamilyCode { get; init; } = string.Empty;
    public string SourceFileName { get; init; } = string.Empty;
    public string SourceFileSha256 { get; init; } = string.Empty;
    public bool AllowWarnings { get; init; }
    public string UpdateStrategy { get; init; } = DynamicStandardUpdateStrategies.Replace;
    public bool ConfirmTemplateCreation { get; init; }
    public StandardTemplateDraftDto? TemplateDraft { get; init; }
    public IReadOnlyList<string> UniqueKeyFields { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> ConflictDecisions { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<DynamicStandardPreviewRowDto> Rows { get; init; } = Array.Empty<DynamicStandardPreviewRowDto>();
}

/// <summary>动态规范导入批次保存结果。</summary>
public sealed class DynamicStandardImportCommitResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string BatchId { get; init; } = string.Empty;
    public int SavedRowCount { get; init; }
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// 模板字段定义及表头别名。
/// </summary>
public sealed class StandardTemplateColumnDto
{
    public long Id { get; init; }
    public long TemplateId { get; init; }
    public string FieldCode { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string DataType { get; init; } = "TEXT";
    public string Unit { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
    public List<string> HeaderAliases { get; init; } = new();
    public string ValidationJson { get; init; } = "{}";
}

/// <summary>
/// 导入批次状态。预览与确认之间不再依赖单机内存。
/// </summary>
public sealed class StandardImportBatchDto
{
    public string BatchId { get; init; } = string.Empty;
    public long SeriesId { get; init; }
    public long? VersionId { get; init; }
    public long? TemplateId { get; init; }
    public string FamilyCode { get; init; } = string.Empty;
    public string Status { get; init; } = "PREVIEW";
    public int RowCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public string SourceFileName { get; init; } = string.Empty;
    public string SourceFileSha256 { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>
/// 模板驱动的 Excel 预览响应，不绑定任何具体部件的数据字段。
/// </summary>
public sealed class DynamicStandardPreviewResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsTemplateMatched { get; init; }
    public StandardTemplateDto? Template { get; init; }
    public IReadOnlyList<DynamicStandardPreviewColumnDto> Columns { get; init; } = Array.Empty<DynamicStandardPreviewColumnDto>();
    public IReadOnlyList<DynamicStandardPreviewRowDto> Rows { get; init; } = Array.Empty<DynamicStandardPreviewRowDto>();
    public IReadOnlyList<string> UnmappedHeaders { get; init; } = Array.Empty<string>();
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public StandardTemplateDraftDto? TemplateDraft { get; init; }
    public DynamicStandardDifferenceDto? Difference { get; init; }
    public bool HasExistingVersion { get; init; }
    public long? ExistingVersionId { get; init; }
    public IReadOnlyList<string> CandidateUniqueKeyFields { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 动态预览的一列；模板命中后使用 FieldCode，否则仅保留原始表头。
/// </summary>
public sealed class DynamicStandardPreviewColumnDto
{
    public string Header { get; init; } = string.Empty;
    public string FieldCode { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string DataType { get; init; } = "TEXT";
    public string Unit { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsMapped { get; init; }
}

/// <summary>
/// 动态预览的一行，字段键为模板 FieldCode 或原始表头。
/// </summary>
public sealed class DynamicStandardPreviewRowDto
{
    public int RowNumber { get; init; }
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// 模板查询和表头匹配的结果。
/// </summary>
public sealed class StandardTemplateMatchResult
{
    public StandardTemplateDto? Template { get; init; }
    public IReadOnlyDictionary<string, StandardTemplateColumnDto> HeaderMappings { get; init; }
        = new Dictionary<string, StandardTemplateColumnDto>(StringComparer.OrdinalIgnoreCase);
}
