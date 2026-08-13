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
