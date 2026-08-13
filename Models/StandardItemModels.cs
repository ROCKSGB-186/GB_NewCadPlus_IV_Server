namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 规范记录的公共字段。专业数据保存在对应的扩展对象中。
/// </summary>
public sealed class StandardItemDto
{
    public long Id { get; init; }
    public long SeriesId { get; init; }
    public long VersionId { get; init; }
    public string FamilyCode { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string DN { get; init; } = string.Empty;
    public int? DNValue { get; init; }
    public string PN { get; init; } = string.Empty;
    public string Material { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty;
    public int SourceRowNumber { get; init; }
    public Dictionary<string, string> RawValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ExtraAttributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// 法兰专业扩展。旧 FlangeStandardRecordDto 仍作为兼容接口保留。
/// </summary>
public sealed class FlangeStandardExtensionDto
{
    public decimal? PipeOuterDiameterSeriesI { get; init; }
    public decimal? PipeOuterDiameterSeriesII { get; init; }
    public decimal? FlangeOuterDiameter { get; init; }
    public decimal? BoltCircleDiameter { get; init; }
    public decimal? BoltHoleDiameter { get; init; }
    public int? BoltCount { get; init; }
    public string BoltSpecification { get; init; } = string.Empty;
    public string BoltRawSuffix { get; init; } = string.Empty;
    public decimal? FlangeThickness { get; init; }
    public decimal? RaisedFaceHeight { get; init; }
    public decimal? FlangeInnerDiameterSeriesI { get; init; }
    public decimal? FlangeInnerDiameterSeriesII { get; init; }
}

/// <summary>
/// 面向查询和 CAD 复用的规范记录结果。
/// </summary>
public sealed class StandardItemMatchResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int MatchCount { get; init; }
    public bool IsUniqueMatch { get; init; }
    public StandardItemDto? Item { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
