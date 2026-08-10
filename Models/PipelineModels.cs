namespace GB_NewCadPlus_IV.UploadApi.Models;

/// <summary>
/// 管道字段契约和管道规范查询模型。
/// 该文件只定义 HTTP 契约，不直接访问数据库，也不包含 CAD 类型。
/// </summary>
public static class PipelineRoles
{
    /// <summary>进口管道角色编码。</summary>
    public const string Import = "IMPORT";

    /// <summary>出口管道角色编码。</summary>
    public const string Export = "EXPORT";
}

/// <summary>
/// 管道参数字段的数据类型名称。
/// 使用字符串传输，兼容 net48 客户端和 net8 服务器。
/// </summary>
public static class PipelineFieldDataTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Select = "select";
    public const string MultiLine = "multiline";
}

/// <summary>
/// 管道通用参数字段定义。
/// </summary>
public sealed class PipelineFieldDefinitionDto
{
    /// <summary>AutoCAD 属性 Tag，也是管道属性字典的标准键。</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>参数页面显示的中文提示。</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>默认值。</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>客户端控件类型名称。</summary>
    public string DataType { get; init; } = PipelineFieldDataTypes.Text;

    /// <summary>是否必填。</summary>
    public bool Required { get; init; }

    /// <summary>是否允许用户编辑。</summary>
    public bool Editable { get; init; } = true;

    /// <summary>参数分组，例如基本信息、设计条件、材料和检验。</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>显示顺序。</summary>
    public int DisplayOrder { get; init; }

    /// <summary>下拉选项；非下拉字段为空数组。</summary>
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 管道角色对应的图面样式。
/// 进口和出口共用参数，只通过此模型控制视觉差异。
/// </summary>
public sealed class PipelineRoleStyleDto
{
    /// <summary>角色编码，例如 IMPORT 或 EXPORT。</summary>
    public string PipeRole { get; init; } = string.Empty;

    /// <summary>角色显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>标题文字颜色的 AutoCAD ACI 颜色索引。</summary>
    public short TitleColorIndex { get; init; }

    /// <summary>流向符号颜色的 AutoCAD ACI 颜色索引。</summary>
    public short FlowDirectionColorIndex { get; init; }

    /// <summary>流向符号名称或资源编码。</summary>
    public string FlowDirectionSymbol { get; init; } = string.Empty;
}

/// <summary>
/// 管道字段目录响应。
/// </summary>
public sealed class PipelineFieldCatalogResponse
{
    /// <summary>响应是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>响应说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>通用管道字段。</summary>
    public IReadOnlyList<PipelineFieldDefinitionDto> Fields { get; init; } = Array.Empty<PipelineFieldDefinitionDto>();

    /// <summary>进口/出口角色样式。</summary>
    public IReadOnlyList<PipelineRoleStyleDto> RoleStyles { get; init; } = Array.Empty<PipelineRoleStyleDto>();
}

/// <summary>
/// 管道默认值响应。
/// </summary>
public sealed class PipelineDefaultsResponse
{
    /// <summary>响应是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>响应说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>按 Tag 返回默认值。</summary>
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 管道 GB 设计规范匹配请求。
/// </summary>
public sealed class PipelineDesignStandardMatchRequest
{
    /// <summary>设计标准，例如 GB/T 20801。</summary>
    public string DrawingStandardNo { get; init; } = string.Empty;

    /// <summary>公称通径，例如 DN150。</summary>
    public string DN { get; init; } = string.Empty;

    /// <summary>公称压力，例如 PN10。</summary>
    public string PN { get; init; } = string.Empty;

    /// <summary>壁厚等级，例如 Sch40。</summary>
    public string Schedule { get; init; } = string.Empty;

    /// <summary>管道材质。</summary>
    public string PipeMaterial { get; init; } = string.Empty;

    /// <summary>介质或介质编码。</summary>
    public string Medium { get; init; } = string.Empty;
}

/// <summary>
/// 管道 GB 设计规范匹配响应。
/// </summary>
public sealed class PipelineDesignStandardMatchResponse
{
    /// <summary>是否匹配成功。</summary>
    public bool Success { get; init; }

    /// <summary>响应说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>匹配数量。</summary>
    public int MatchCount { get; init; }

    /// <summary>是否唯一匹配。</summary>
    public bool IsUniqueMatch { get; init; }

    /// <summary>由设计规范计算或确定的管道属性。</summary>
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>规范来源信息。</summary>
    public string StandardNumber { get; init; } = string.Empty;
}

/// <summary>
/// 可配置的管道设计规范记录。
/// 当前用于承载服务器配置中的真实规范记录，不在代码中伪造工程数据。
/// </summary>
public sealed class PipelineDesignStandardRecordDto
{
    /// <summary>设计标准号。</summary>
    public string DrawingStandardNo { get; init; } = string.Empty;

    /// <summary>公称通径。</summary>
    public string DN { get; init; } = string.Empty;

    /// <summary>公称压力。</summary>
    public string PN { get; init; } = string.Empty;

    /// <summary>壁厚等级。</summary>
    public string Schedule { get; init; } = string.Empty;

    /// <summary>管道材质。</summary>
    public string PipeMaterial { get; init; } = string.Empty;

    /// <summary>介质或介质编码。</summary>
    public string Medium { get; init; } = string.Empty;

    /// <summary>匹配后写入管道的属性。</summary>
    public Dictionary<string, string> Attributes { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}
