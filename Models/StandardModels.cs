namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 规范系列信息，例如“GB/T 9124.1-2019 表52 PN10 板式平焊钢制管法兰”。
/// </summary>
public sealed class StandardSeriesDto
{
    /// <summary>规范系列唯一标识。</summary>
    public long Id { get; init; }

    /// <summary>规范系列所属分类 ID。</summary>
    public long? CategoryId { get; init; }

    /// <summary>规范大类编码，例如 FLANGE。</summary>
    public string FamilyCode { get; init; } = string.Empty;

    /// <summary>规范大类名称，例如 法兰。</summary>
    public string FamilyName { get; init; } = string.Empty;

    /// <summary>系列编码，例如 PLATE_WELD。</summary>
    public string SeriesCode { get; init; } = string.Empty;

    /// <summary>系列显示名称。</summary>
    public string SeriesName { get; init; } = string.Empty;

    /// <summary>标准号，例如 GB/T 9124.1-2019。</summary>
    public string StandardNumber { get; init; } = string.Empty;

    /// <summary>标准表号，例如 表52。</summary>
    public string TableNumber { get; init; } = string.Empty;

    /// <summary>压力等级筛选条件，例如 PN10。</summary>
    public string PressureRating { get; init; } = string.Empty;

    /// <summary>法兰类型，例如 PL。</summary>
    public string FlangeType { get; init; } = string.Empty;

    /// <summary>密封面型式，例如 RF。</summary>
    public string FaceType { get; init; } = string.Empty;

    /// <summary>是否启用。</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// JSON 规范导入文档，文件内同时保存系列元数据和法兰记录。
/// </summary>
public sealed class StandardJsonImportDocumentDto
{
    /// <summary>规范大类编码。</summary>
    public string FamilyCode { get; init; } = "FLANGE";

    /// <summary>规范大类名称。</summary>
    public string FamilyName { get; init; } = "法兰";

    /// <summary>规范系列编码。</summary>
    public string SeriesCode { get; init; } = "PLATE_WELD";

    /// <summary>规范系列名称。</summary>
    public string SeriesName { get; init; } = "板式平焊钢制管法兰";

    /// <summary>标准号。</summary>
    public string StandardNumber { get; init; } = string.Empty;

    /// <summary>表号。</summary>
    public string TableNumber { get; init; } = string.Empty;

    /// <summary>压力等级。</summary>
    public string PressureRating { get; init; } = string.Empty;

    /// <summary>法兰类型。</summary>
    public string FlangeType { get; init; } = "PL";

    /// <summary>密封面型式。</summary>
    public string FaceType { get; init; } = "RF";

    /// <summary>法兰规范记录。</summary>
    public List<FlangeStandardRecordDto> Records { get; init; } = new();
}

/// <summary>
/// 法兰规范记录，保存一个 DN 在两个钢管系列下的完整尺寸数据。
/// </summary>
public sealed class FlangeStandardRecordDto
{
    /// <summary>数据库记录主键。</summary>
    public long Id { get; init; }

    /// <summary>所属规范系列 ID。</summary>
    public long SeriesId { get; init; }

    /// <summary>规范中的原始行号，便于回溯 Excel 或标准表。</summary>
    public int SourceRowNumber { get; init; }

    /// <summary>公称尺寸，外部保存为 DN50 形式。</summary>
    public string DN { get; init; } = string.Empty;

    /// <summary>用于服务器排序和查询的 DN 数值。</summary>
    public int DNValue { get; init; }

    /// <summary>PN 筛选条件，例如 PN10。</summary>
    public string PN { get; init; } = string.Empty;

    /// <summary>钢管外径 A，系列 I。</summary>
    public decimal? PipeOuterDiameterSeriesI { get; init; }

    /// <summary>钢管外径 A，系列 II。</summary>
    public decimal? PipeOuterDiameterSeriesII { get; init; }

    /// <summary>法兰外径 D。</summary>
    public decimal? FlangeOuterDiameter { get; init; }

    /// <summary>螺栓孔中心圆直径 K。</summary>
    public decimal? BoltCircleDiameter { get; init; }

    /// <summary>螺栓孔直径 L。</summary>
    public decimal? BoltHoleDiameter { get; init; }

    /// <summary>螺栓数量 n。</summary>
    public int? BoltCount { get; init; }

    /// <summary>螺栓规格，例如 M16。</summary>
    public string BoltSpecification { get; init; } = string.Empty;

    /// <summary>大口径标准表中可能出现的原始螺栓附加文本，暂不猜测含义。</summary>
    public string? BoltRawSuffix { get; init; }

    /// <summary>法兰厚度 C。</summary>
    public decimal? FlangeThickness { get; init; }

    /// <summary>突面高度 f1。</summary>
    public decimal? RaisedFaceHeight { get; init; }

    /// <summary>法兰内径 B，系列 I。</summary>
    public decimal? FlangeInnerDiameterSeriesI { get; init; }

    /// <summary>法兰内径 B，系列 II。</summary>
    public decimal? FlangeInnerDiameterSeriesII { get; init; }

    /// <summary>导入时的原始值和校验信息。</summary>
    public Dictionary<string, string> RawValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>导入校验警告，不阻止预览，但确认导入时必须显示。</summary>
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// 规范查询条件。
/// </summary>
public sealed class StandardMatchRequest
{
    /// <summary>规范大类编码，例如 FLANGE。</summary>
    public string FamilyCode { get; init; } = "FLANGE";

    /// <summary>规范系列编码，例如 PLATE_WELD。</summary>
    public string SeriesCode { get; init; } = string.Empty;

    /// <summary>标准号，可选；为空时按系列编码查询。</summary>
    public string? StandardNumber { get; init; }

    /// <summary>表号，可选。</summary>
    public string? TableNumber { get; init; }

    /// <summary>PN 筛选条件。</summary>
    public string? PN { get; init; }

    /// <summary>公称尺寸，建议传 DN50。</summary>
    public string DN { get; init; } = string.Empty;

    /// <summary>钢管系列，支持 Ⅰ系列或Ⅱ系列。</summary>
    public string Series { get; init; } = "Ⅰ系列";

    /// <summary>法兰类型，例如 PL。</summary>
    public string? FlangeType { get; init; }

    /// <summary>密封面型式，例如 RF。</summary>
    public string? FaceType { get; init; }
}

/// <summary>
/// 规范查询返回的可直接写入法兰图元的属性集合。
/// </summary>
public sealed class StandardMatchResponse
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>业务说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>匹配数量。</summary>
    public int MatchCount { get; init; }

    /// <summary>是否唯一匹配。</summary>
    public bool IsUniqueMatch { get; init; }

    /// <summary>当前选定钢管系列下的 AutoCAD 属性值。</summary>
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>完整规范记录，包含系列 I/II 数据。</summary>
    public FlangeStandardRecordDto? Record { get; init; }
}

/// <summary>
/// Excel 导入预览行，后续预览和确认接口共用。
/// </summary>
public sealed class StandardImportRowDto
{
    /// <summary>Excel 行号。</summary>
    public int RowNumber { get; init; }

    /// <summary>规范记录。</summary>
    public FlangeStandardRecordDto? Record { get; init; }

    /// <summary>该行的错误信息。</summary>
    public List<string> Errors { get; init; } = new();

    /// <summary>该行的警告信息。</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>是否可以进入确认导入阶段。</summary>
    public bool IsValid => Record != null && Errors.Count == 0;
}

/// <summary>
/// 规范导入批次预览结果。
/// </summary>
public sealed class StandardImportPreviewResponse
{
    /// <summary>是否解析成功。</summary>
    public bool Success { get; init; }

    /// <summary>说明信息。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>导入批次临时标识。</summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>预览行。</summary>
    public IReadOnlyList<StandardImportRowDto> Rows { get; init; } = Array.Empty<StandardImportRowDto>();

    /// <summary>错误总数。</summary>
    public int ErrorCount { get; init; }

    /// <summary>警告总数。</summary>
    public int WarningCount { get; init; }
}
