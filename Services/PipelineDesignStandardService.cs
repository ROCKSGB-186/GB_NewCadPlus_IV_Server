using GB_NewCadPlus_IV.UploadApi.Models;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 管道 GB 设计规范匹配服务。
/// 当前从 PipelineStandards:Records 配置读取记录，后续可替换为数据库查询而不改变 API 契约。
/// </summary>
public sealed class PipelineDesignStandardService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PipelineDesignStandardService> _logger;

    /// <summary>
    /// 创建管道设计规范服务。
    /// </summary>
    public PipelineDesignStandardService(
        IConfiguration configuration,
        ILogger<PipelineDesignStandardService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据标准号、DN、PN、壁厚等级、材质和介质匹配唯一规范记录。
    /// </summary>
    public PipelineDesignStandardMatchResponse Match(PipelineDesignStandardMatchRequest request)
    {
        ValidateRequest(request);

        List<PipelineDesignStandardRecordDto> records = ReadConfiguredRecords();
        List<PipelineDesignStandardRecordDto> matches = records
            .Where(record => IsMatch(record, request))
            .ToList();

        _logger.LogInformation(
            "管道 GB 设计规范匹配完成：StandardNo={StandardNo}, DN={DN}, PN={PN}, Schedule={Schedule}, Material={Material}, Medium={Medium}, ConfiguredCount={ConfiguredCount}, MatchCount={MatchCount}",
            request.DrawingStandardNo,
            request.DN,
            request.PN,
            request.Schedule,
            request.PipeMaterial,
            request.Medium,
            records.Count,
            matches.Count);

        if (matches.Count == 0)
        {
            return new PipelineDesignStandardMatchResponse
            {
                Success = false,
                Message = "未找到匹配的管道 GB 设计规范。请先配置真实的管道规范记录。",
                MatchCount = 0,
                IsUniqueMatch = false,
                StandardNumber = request.DrawingStandardNo
            };
        }

        if (matches.Count > 1)
        {
            return new PipelineDesignStandardMatchResponse
            {
                Success = false,
                Message = $"匹配到 {matches.Count} 条管道 GB 设计规范，条件不唯一，请补充查询条件。",
                MatchCount = matches.Count,
                IsUniqueMatch = false,
                StandardNumber = request.DrawingStandardNo
            };
        }

        PipelineDesignStandardRecordDto match = matches[0];
        Dictionary<string, string> attributes = new(match.Attributes, StringComparer.OrdinalIgnoreCase);
        AddIfMissing(attributes, "DRAWINGNO.STANDARDNO", match.DrawingStandardNo);
        AddIfMissing(attributes, "DN", match.DN);
        AddIfMissing(attributes, "PN", match.PN);
        AddIfMissing(attributes, "SCHEDULE", match.Schedule);
        AddIfMissing(attributes, "PIPE_MATL", match.PipeMaterial);
        AddIfMissing(attributes, "MEDIUM", match.Medium);

        foreach (KeyValuePair<string, string> attribute in attributes)
        {
            _logger.LogInformation(
                "管道规范返回属性：Tag={Tag}, Value={Value}",
                attribute.Key,
                attribute.Value);
        }

        return new PipelineDesignStandardMatchResponse
        {
            Success = true,
            Message = "管道 GB 设计规范匹配成功。",
            MatchCount = 1,
            IsUniqueMatch = true,
            StandardNumber = match.DrawingStandardNo,
            Attributes = attributes
        };
    }

    /// <summary>
    /// 从配置节点读取真实规范记录。
    /// 配置为空时返回空集合，避免用示例数据冒充工程标准。
    /// </summary>
    private List<PipelineDesignStandardRecordDto> ReadConfiguredRecords()
    {
        IConfigurationSection recordsSection = _configuration.GetSection("PipelineStandards:Records");
        List<PipelineDesignStandardRecordDto> records = new();

        foreach (IConfigurationSection recordSection in recordsSection.GetChildren())
        {
            Dictionary<string, string> attributes = new(StringComparer.OrdinalIgnoreCase);
            IConfigurationSection attributesSection = recordSection.GetSection("Attributes");
            foreach (IConfigurationSection attributeSection in attributesSection.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(attributeSection.Key))
                {
                    attributes[attributeSection.Key] = attributeSection.Value ?? string.Empty;
                }
            }

            records.Add(new PipelineDesignStandardRecordDto
            {
                DrawingStandardNo = recordSection["DrawingStandardNo"] ?? string.Empty,
                DN = recordSection["DN"] ?? string.Empty,
                PN = recordSection["PN"] ?? string.Empty,
                Schedule = recordSection["Schedule"] ?? string.Empty,
                PipeMaterial = recordSection["PipeMaterial"] ?? string.Empty,
                Medium = recordSection["Medium"] ?? string.Empty,
                Attributes = attributes
            });
        }

        return records;
    }

    /// <summary>
    /// 校验查询请求，避免空条件导致返回错误规范。
    /// </summary>
    private static void ValidateRequest(PipelineDesignStandardMatchRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DrawingStandardNo))
        {
            throw new ArgumentException("设计标准号不能为空。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DN))
        {
            throw new ArgumentException("DN 不能为空。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.PN))
        {
            throw new ArgumentException("PN 不能为空。", nameof(request));
        }
    }

    /// <summary>
    /// 按非空查询条件进行不区分大小写匹配。
    /// </summary>
    private static bool IsMatch(
        PipelineDesignStandardRecordDto record,
        PipelineDesignStandardMatchRequest request)
    {
        return EqualsNormalized(record.DrawingStandardNo, request.DrawingStandardNo)
            && EqualsNormalized(record.DN, request.DN)
            && EqualsNormalized(record.PN, request.PN)
            && OptionalEquals(record.Schedule, request.Schedule)
            && OptionalEquals(record.PipeMaterial, request.PipeMaterial)
            && OptionalEquals(record.Medium, request.Medium);
    }

    /// <summary>
    /// 比较必填规范条件。
    /// </summary>
    private static bool EqualsNormalized(string left, string right)
    {
        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 比较可选规范条件；请求为空时不限制该字段。
    /// </summary>
    private static bool OptionalEquals(string recordValue, string requestValue)
    {
        return string.IsNullOrWhiteSpace(requestValue)
            || EqualsNormalized(recordValue, requestValue);
    }

    /// <summary>
    /// 统一清洗用户和规范记录中的文本。
    /// </summary>
    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim();
    }

    /// <summary>
    /// 仅在规范记录未返回该字段时补充查询条件，避免覆盖规范明细值。
    /// </summary>
    private static void AddIfMissing(
        IDictionary<string, string> attributes,
        string key,
        string value)
    {
        if (!attributes.ContainsKey(key) && !string.IsNullOrWhiteSpace(value))
        {
            attributes[key] = value;
        }
    }
}
