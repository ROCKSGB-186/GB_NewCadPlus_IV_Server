namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 规范身份定位请求。
/// 这些字段只用于定位 STANDARD_SERIES，不用于查询具体 DN 数据行。
/// </summary>
public sealed class StandardIdentityResolveRequest
{
    /// <summary>部件大类编码，例如 FLANGE。</summary>
    public string FamilyCode { get; init; } = string.Empty;

    /// <summary>规范系列编码，例如 PLATE_WELD。</summary>
    public string SeriesCode { get; init; } = string.Empty;

    /// <summary>标准号，例如 GB/T 9124.1-2019。</summary>
    public string StandardNumber { get; init; } = string.Empty;

    /// <summary>标准表号，例如 表52；没有表号时传空字符串。</summary>
    public string TableNumber { get; init; } = string.Empty;

    /// <summary>压力等级，例如 PN10；没有压力等级时传空字符串。</summary>
    public string PressureRating { get; init; } = string.Empty;
}

/// <summary>
/// 规范身份定位响应。
/// Exists 表示是否存在唯一规范系列，CurrentVersion 表示该系列当前版本。
/// </summary>
public sealed class StandardIdentityResolveResponse
{
    /// <summary>请求是否正常处理。</summary>
    public bool Success { get; init; }

    /// <summary>是否找到规范系列。</summary>
    public bool Exists { get; init; }

    /// <summary>匹配到的规范系列数量。</summary>
    public int MatchCount { get; init; }

    /// <summary>是否唯一匹配。</summary>
    public bool IsUniqueMatch { get; init; }

    /// <summary>业务提示。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>匹配到的规范系列；未命中或多条命中时为空。</summary>
    public StandardManagementSeriesDto? Series { get; init; }

    /// <summary>规范系列的当前版本；没有当前版本时为空。</summary>
    public StandardDocumentVersionDto? CurrentVersion { get; init; }
}

/// <summary>
/// 导入预览阶段的规范身份判断结果。
/// </summary>
public sealed class StandardImportIdentityResultDto
{
    /// <summary>身份判断状态：NEW、EXISTING 或 CONFLICT。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>匹配到的规范系列数量。</summary>
    public int MatchCount { get; init; }

    /// <summary>是否可以唯一确定已有规范系列。</summary>
    public bool IsUniqueMatch { get; init; }

    /// <summary>给预览页面显示的说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>唯一命中时的规范系列。</summary>
    public StandardManagementSeriesDto? ExistingSeries { get; init; }

    /// <summary>唯一命中时的当前版本。</summary>
    public StandardDocumentVersionDto? CurrentVersion { get; init; }
}
