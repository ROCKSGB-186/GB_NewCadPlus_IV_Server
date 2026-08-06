using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 规范查询服务。
/// 规范数据只由服务器访问，客户端通过 HTTP 接口获得匹配结果。
/// </summary>
public sealed class StandardQueryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StandardQueryService> _logger;

    /// <summary>
    /// 创建规范查询服务。
    /// </summary>
    public StandardQueryService(IConfiguration configuration, ILogger<StandardQueryService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询唯一的法兰规范记录，并转换为当前法兰图元可使用的属性。
    /// </summary>
    public async Task<StandardMatchResponse> MatchFlangeAsync(
        StandardMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string familyCode = NormalizeCode(request.FamilyCode);
        string seriesCode = NormalizeCode(request.SeriesCode);
        string dn = NormalizeDn(request.DN);
        string pn = NormalizeCode(request.PN);

        _logger.LogInformation(
            "收到匹配请求：FamilyCode={FamilyCode}, SeriesCode={SeriesCode}, StandardNumber={StandardNumber}, TableNumber={TableNumber}, DN={DN}, PN={PN}, Series={Series}, FlangeType={FlangeType}, FaceType={FaceType}",
            familyCode,
            seriesCode,
            NormalizeOptional(request.StandardNumber) ?? "(空)",
            NormalizeOptional(request.TableNumber) ?? "(空)",
            dn,
            pn,
            request.Series,
            NormalizeOptional(request.FlangeType) ?? "(空)",
            NormalizeOptional(request.FaceType) ?? "(空)");

        if (familyCode != "FLANGE")
        {
            throw new ArgumentException("第一阶段只支持 FLANGE 法兰规范查询。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(seriesCode))
        {
            throw new ArgumentException("规范系列编码不能为空。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(dn))
        {
            throw new ArgumentException("DN 不能为空，格式应为 DN50。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(pn))
        {
            throw new ArgumentException("PN 不能为空，格式应为 PN10。", nameof(request));
        }

        int dnValue = ParseDnValue(dn);
        string selectedSeries = NormalizeSeries(request.Series);
        string databaseType = GetDatabaseType();

        _logger.LogInformation(
            "请求校验通过：DNValue={DNValue}, SelectedSeries={SelectedSeries}, DatabaseType={DatabaseType}, Schema={Schema}",
            dnValue,
            selectedSeries,
            databaseType,
            databaseType == "DM" ? GetSchemaName() : "(MySQL database)");

        try
        {
            StandardMatchData? data = databaseType == "DM"
                ? await QueryDmAsync(familyCode, seriesCode, request, dn, dnValue, pn, cancellationToken).ConfigureAwait(false)
                : await QueryMySqlAsync(familyCode, seriesCode, request, dn, dnValue, pn, cancellationToken).ConfigureAwait(false);

            if (data == null)
            {
                _logger.LogWarning(
                    "数据库未命中：FamilyCode={FamilyCode}, SeriesCode={SeriesCode}, DN={DN}, PN={PN}",
                    familyCode,
                    seriesCode,
                    dn,
                    pn);
                return new StandardMatchResponse
                {
                    Success = false,
                    Message = $"未找到法兰规范：系列={seriesCode}，DN={dn}，PN={pn}。",
                    MatchCount = 0,
                    IsUniqueMatch = false
                };
            }

            FlangeStandardRecordDto record = data.Record;
            Dictionary<string, string> attributes = ToCadAttributes(data.Series, record, selectedSeries);

            _logger.LogInformation(
                "数据库命中：SeriesId={SeriesId}, RecordId={RecordId}, DN={DN}, PN={PN}, AttributeCount={AttributeCount}",
                data.Series.Id,
                record.Id,
                record.DN,
                record.PN,
                attributes.Count);
            foreach (KeyValuePair<string, string> attribute in attributes)
            {
                _logger.LogInformation("返回CAD属性：{AttributeKey}={AttributeValue}", attribute.Key, attribute.Value);
            }

            return new StandardMatchResponse
            {
                Success = true,
                Message = "法兰规范匹配成功。",
                MatchCount = 1,
                IsUniqueMatch = true,
                Attributes = attributes,
                Record = record
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "查询法兰规范失败。SeriesCode={SeriesCode}, DN={DN}, PN={PN}, DatabaseType={DatabaseType}",
                seriesCode,
                dn,
                pn,
                databaseType);
            throw;
        }
    }

    private async Task<StandardMatchData?> QueryMySqlAsync(
        string familyCode,
        string seriesCode,
        StandardMatchRequest request,
        string dn,
        int dnValue,
        string pn,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    ss.id AS SeriesId,
    sf.code AS FamilyCode,
    sf.name AS FamilyName,
    ss.series_code AS SeriesCode,
    ss.series_name AS SeriesName,
    ss.standard_number AS StandardNumber,
    ss.table_number AS TableNumber,
    ss.pressure_rating AS PressureRating,
    ss.flange_type AS FlangeType,
    ss.face_type AS FaceType,
    sfr.id AS RecordId,
    sfr.source_row_number AS SourceRowNumber,
    sfr.dn AS DN,
    sfr.dn_value AS DNValue,
    sfr.pn AS PN,
    sfr.pipe_outer_diameter_i AS PipeOuterDiameterSeriesI,
    sfr.pipe_outer_diameter_ii AS PipeOuterDiameterSeriesII,
    sfr.flange_outer_diameter AS FlangeOuterDiameter,
    sfr.bolt_circle_diameter AS BoltCircleDiameter,
    sfr.bolt_hole_diameter AS BoltHoleDiameter,
    sfr.bolt_count AS BoltCount,
    sfr.bolt_specification AS BoltSpecification,
    sfr.bolt_raw_suffix AS BoltRawSuffix,
    sfr.flange_thickness AS FlangeThickness,
    sfr.raised_face_height AS RaisedFaceHeight,
    sfr.flange_inner_diameter_i AS FlangeInnerDiameterSeriesI,
    sfr.flange_inner_diameter_ii AS FlangeInnerDiameterSeriesII,
    sfr.raw_values_json AS RawValuesJson,
    sfr.warnings_json AS WarningsJson
FROM standard_flange_records sfr
INNER JOIN standard_series ss ON ss.id = sfr.series_id AND ss.is_active = 1
INNER JOIN standard_families sf ON sf.id = ss.family_id AND sf.is_active = 1
WHERE sf.code = @FamilyCode
  AND ss.series_code = @SeriesCode
  AND sfr.dn = @DN
  AND sfr.dn_value = @DNValue
  AND sfr.pn = @PN
  AND (@StandardNumber IS NULL OR ss.standard_number = @StandardNumber)
  AND (@TableNumber IS NULL OR ss.table_number = @TableNumber)
  AND (@FlangeType IS NULL OR ss.flange_type = @FlangeType)
  AND (@FaceType IS NULL OR ss.face_type = @FaceType)
  AND sfr.is_active = 1
LIMIT 1";

        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "开始 MySQL 查询：FamilyCode={FamilyCode}, SeriesCode={SeriesCode}, DN={DN}, DNValue={DNValue}, PN={PN}",
            familyCode,
            seriesCode,
            dn,
            dnValue,
            pn);

        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MySQL 数据库连接已打开。");
        StandardMatchRow? row = await connection.QuerySingleOrDefaultAsync<StandardMatchRow>(
            new CommandDefinition(sql, new
            {
                FamilyCode = familyCode,
                SeriesCode = seriesCode,
                DN = dn,
                DNValue = dnValue,
                PN = pn,
                StandardNumber = NormalizeOptional(request.StandardNumber),
                TableNumber = NormalizeOptional(request.TableNumber),
                FlangeType = NormalizeOptional(request.FlangeType),
                FaceType = NormalizeOptional(request.FaceType)
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        stopwatch.Stop();
        _logger.LogInformation(
            "MySQL 查询完成：是否命中={Matched}, 耗时Ms={ElapsedMilliseconds}",
            row != null,
            stopwatch.ElapsedMilliseconds);

        return row == null ? null : ToMatchData(row);
    }

    private async Task<StandardMatchData?> QueryDmAsync(
        string familyCode,
        string seriesCode,
        StandardMatchRequest request,
        string dn,
        int dnValue,
        string pn,
        CancellationToken cancellationToken)
    {
        string schema = GetSchemaName();
        const string sqlTemplate = @"
SELECT
    ss.ID AS SeriesId,
    sf.CODE AS FamilyCode,
    sf.NAME AS FamilyName,
    ss.SERIES_CODE AS SeriesCode,
    ss.SERIES_NAME AS SeriesName,
    ss.STANDARD_NUMBER AS StandardNumber,
    ss.TABLE_NUMBER AS TableNumber,
    ss.PRESSURE_RATING AS PressureRating,
    ss.FLANGE_TYPE AS FlangeType,
    ss.FACE_TYPE AS FaceType,
    sfr.ID AS RecordId,
    sfr.SOURCE_ROW_NUMBER AS SourceRowNumber,
    sfr.DN AS DN,
    sfr.DN_VALUE AS DNValue,
    sfr.PN AS PN,
    sfr.PIPE_OUTER_DIAMETER_I AS PipeOuterDiameterSeriesI,
    sfr.PIPE_OUTER_DIAMETER_II AS PipeOuterDiameterSeriesII,
    sfr.FLANGE_OUTER_DIAMETER AS FlangeOuterDiameter,
    sfr.BOLT_CIRCLE_DIAMETER AS BoltCircleDiameter,
    sfr.BOLT_HOLE_DIAMETER AS BoltHoleDiameter,
    sfr.BOLT_COUNT AS BoltCount,
    sfr.BOLT_SPECIFICATION AS BoltSpecification,
    sfr.BOLT_RAW_SUFFIX AS BoltRawSuffix,
    sfr.FLANGE_THICKNESS AS FlangeThickness,
    sfr.RAISED_FACE_HEIGHT AS RaisedFaceHeight,
    sfr.FLANGE_INNER_DIAMETER_I AS FlangeInnerDiameterSeriesI,
    sfr.FLANGE_INNER_DIAMETER_II AS FlangeInnerDiameterSeriesII,
    sfr.RAW_VALUES_JSON AS RawValuesJson,
    sfr.WARNINGS_JSON AS WarningsJson
FROM {0}.STANDARD_FLANGE_RECORDS sfr
INNER JOIN {0}.STANDARD_SERIES ss ON ss.ID = sfr.SERIES_ID AND ss.IS_ACTIVE = 1
INNER JOIN {0}.STANDARD_FAMILIES sf ON sf.ID = ss.FAMILY_ID AND sf.IS_ACTIVE = 1
WHERE sf.CODE = :FamilyCode
  AND ss.SERIES_CODE = :SeriesCode
  AND sfr.DN = :DN
  AND sfr.DN_VALUE = :DNValue
  AND sfr.PN = :PN
  AND (:StandardNumber IS NULL OR ss.STANDARD_NUMBER = :StandardNumber)
  AND (:TableNumber IS NULL OR ss.TABLE_NUMBER = :TableNumber)
  AND (:FlangeType IS NULL OR ss.FLANGE_TYPE = :FlangeType)
  AND (:FaceType IS NULL OR ss.FACE_TYPE = :FaceType)
  AND sfr.IS_ACTIVE = 1
FETCH FIRST 1 ROWS ONLY";

        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "开始 DM 查询：Schema={Schema}, FamilyCode={FamilyCode}, SeriesCode={SeriesCode}, DN={DN}, DNValue={DNValue}, PN={PN}",
            schema,
            familyCode,
            seriesCode,
            dn,
            dnValue,
            pn);

        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("DM 数据库连接已打开。");
        StandardMatchRow? row = await connection.QuerySingleOrDefaultAsync<StandardMatchRow>(
            new CommandDefinition(string.Format(CultureInfo.InvariantCulture, sqlTemplate, schema), new
            {
                FamilyCode = familyCode,
                SeriesCode = seriesCode,
                DN = dn,
                DNValue = dnValue,
                PN = pn,
                StandardNumber = NormalizeOptional(request.StandardNumber),
                TableNumber = NormalizeOptional(request.TableNumber),
                FlangeType = NormalizeOptional(request.FlangeType),
                FaceType = NormalizeOptional(request.FaceType)
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        stopwatch.Stop();
        _logger.LogInformation(
            "DM 查询完成：是否命中={Matched}, 耗时Ms={ElapsedMilliseconds}",
            row != null,
            stopwatch.ElapsedMilliseconds);

        return row == null ? null : ToMatchData(row);
    }

    private static Dictionary<string, string> ToCadAttributes(
        StandardSeriesData series,
        FlangeStandardRecordDto record,
        string selectedSeries)
    {
        decimal? flangeInnerDiameter = selectedSeries == "Ⅱ系列"
            ? record.FlangeInnerDiameterSeriesII
            : record.FlangeInnerDiameterSeriesI;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DN"] = record.DN,
            ["PN"] = record.PN,
            ["FLG_TYPE"] = series.FlangeType ?? "PL",
            ["FACE_TYPE"] = series.FaceType ?? "RF",
            ["FLG_STD"] = series.StandardNumber,
            ["DRAWINGNO.STANDARDNO"] = series.StandardNumber,
            ["SERIES"] = selectedSeries,
            ["FLG_OD"] = FormatNumber(record.FlangeOuterDiameter),
            ["BOLT_PCD"] = FormatNumber(record.BoltCircleDiameter),
            ["BOLT_HOLE_DIA"] = FormatNumber(record.BoltHoleDiameter),
            ["BOLT_HOLES"] = record.BoltCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["BOLT_SPEC"] = record.BoltSpecification,
            ["FLG_THK"] = FormatNumber(record.FlangeThickness),
            ["FLG_ID"] = FormatNumber(flangeInnerDiameter),
            ["RAISED_FACE_HGT"] = FormatNumber(record.RaisedFaceHeight)
        };
    }

    private static StandardMatchData ToMatchData(StandardMatchRow row)
    {
        var record = new FlangeStandardRecordDto
        {
            Id = row.RecordId,
            SeriesId = row.SeriesId,
            SourceRowNumber = row.SourceRowNumber,
            DN = row.DN,
            DNValue = row.DNValue,
            PN = row.PN,
            PipeOuterDiameterSeriesI = row.PipeOuterDiameterSeriesI,
            PipeOuterDiameterSeriesII = row.PipeOuterDiameterSeriesII,
            FlangeOuterDiameter = row.FlangeOuterDiameter,
            BoltCircleDiameter = row.BoltCircleDiameter,
            BoltHoleDiameter = row.BoltHoleDiameter,
            BoltCount = row.BoltCount,
            BoltSpecification = row.BoltSpecification ?? string.Empty,
            BoltRawSuffix = row.BoltRawSuffix,
            FlangeThickness = row.FlangeThickness,
            RaisedFaceHeight = row.RaisedFaceHeight,
            FlangeInnerDiameterSeriesI = row.FlangeInnerDiameterSeriesI,
            FlangeInnerDiameterSeriesII = row.FlangeInnerDiameterSeriesII,
            RawValues = DeserializeDictionary(row.RawValuesJson),
            Warnings = DeserializeList(row.WarningsJson)
        };

        return new StandardMatchData(
            new StandardSeriesData
            {
                Id = row.SeriesId,
                FamilyCode = row.FamilyCode,
                FamilyName = row.FamilyName,
                SeriesCode = row.SeriesCode,
                SeriesName = row.SeriesName,
                StandardNumber = row.StandardNumber,
                TableNumber = row.TableNumber,
                PressureRating = row.PressureRating,
                FlangeType = row.FlangeType,
                FaceType = row.FaceType
            },
            record);
    }

    private string GetDatabaseType() =>
        (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";

    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || !schema.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new InvalidOperationException("Database:Schema 配置无效。");
        }

        return schema.ToUpperInvariant();
    }

    private string GetConnectionString(string databaseType)
    {
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = (_configuration.GetConnectionString(databaseType == "MYSQL" ? "MySQL" : "DM") ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        }

        return connectionString;
    }

    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value)
    {
        string normalized = NormalizeCode(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeDn(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
        if (normalized.StartsWith("DN", StringComparison.Ordinal))
        {
            return normalized;
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dnValue)
            ? $"DN{dnValue}"
            : normalized;
    }

    private static int ParseDnValue(string dn)
    {
        if (!int.TryParse(dn[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new ArgumentException("DN 必须是 DN10、DN50 等有效格式。", nameof(dn));
        }

        return value;
    }

    private static string NormalizeSeries(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Contains('Ⅱ') || normalized.Contains("II", StringComparison.OrdinalIgnoreCase)
            ? "Ⅱ系列"
            : "Ⅰ系列";
    }

    private static string FormatNumber(decimal? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;

    private static Dictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private sealed class StandardMatchRow
    {
        public long SeriesId { get; init; }
        public string FamilyCode { get; init; } = string.Empty;
        public string FamilyName { get; init; } = string.Empty;
        public string SeriesCode { get; init; } = string.Empty;
        public string SeriesName { get; init; } = string.Empty;
        public string StandardNumber { get; init; } = string.Empty;
        public string TableNumber { get; init; } = string.Empty;
        public string PressureRating { get; init; } = string.Empty;
        public string? FlangeType { get; init; }
        public string? FaceType { get; init; }
        public long RecordId { get; init; }
        public int SourceRowNumber { get; init; }
        public string DN { get; init; } = string.Empty;
        public int DNValue { get; init; }
        public string PN { get; init; } = string.Empty;
        public decimal? PipeOuterDiameterSeriesI { get; init; }
        public decimal? PipeOuterDiameterSeriesII { get; init; }
        public decimal? FlangeOuterDiameter { get; init; }
        public decimal? BoltCircleDiameter { get; init; }
        public decimal? BoltHoleDiameter { get; init; }
        public int? BoltCount { get; init; }
        public string? BoltSpecification { get; init; }
        public string? BoltRawSuffix { get; init; }
        public decimal? FlangeThickness { get; init; }
        public decimal? RaisedFaceHeight { get; init; }
        public decimal? FlangeInnerDiameterSeriesI { get; init; }
        public decimal? FlangeInnerDiameterSeriesII { get; init; }
        public string? RawValuesJson { get; init; }
        public string? WarningsJson { get; init; }
    }

    private sealed class StandardSeriesData
    {
        public long Id { get; init; }
        public string FamilyCode { get; init; } = string.Empty;
        public string FamilyName { get; init; } = string.Empty;
        public string SeriesCode { get; init; } = string.Empty;
        public string SeriesName { get; init; } = string.Empty;
        public string StandardNumber { get; init; } = string.Empty;
        public string TableNumber { get; init; } = string.Empty;
        public string PressureRating { get; init; } = string.Empty;
        public string? FlangeType { get; init; }
        public string? FaceType { get; init; }
    }

    private sealed record StandardMatchData(StandardSeriesData Series, FlangeStandardRecordDto Record);
}
