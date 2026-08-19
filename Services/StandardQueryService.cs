using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 规范查询服务。
/// 规范数据只由服务器访问，客户端通过 HTTP 接口获得匹配结果。
/// </summary>
public sealed class StandardQueryService
{
    /// <summary>
    /// 配置对象。
    /// </summary>
    private readonly IConfiguration _configuration;
    /// <summary>
    /// 日志记录器。
    /// </summary>
    private readonly ILogger<StandardQueryService> _logger;

    /// <summary>
    /// 创建规范查询服务。
    /// </summary>
    /// <param name="configuration">配置对象。</param>
    /// <param name="logger">日志记录器。</param>
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
        string pn = NormalizePn(request.PN);

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

            // 动态导入规范保存在版本行 JSON 中，不会写入静态 STANDARD_FLANGE_RECORDS。
            // 静态表未命中时继续查询当前动态版本，确保规范树中已导入的法兰表也能参与插入匹配。
            if (data == null)
            {
                data = await QueryDynamicAsync(
                    familyCode,
                    seriesCode,
                    request,
                    dn,
                    dnValue,
                    pn,
                    databaseType,
                    cancellationToken).ConfigureAwait(false);
            }

            if (data == null)
            {
                _logger.LogWarning(
                    "数据库未命中：FamilyCode={FamilyCode}, SeriesCode={SeriesCode}, StandardNumber={StandardNumber}, DN={DN}, PN={PN}",
                    familyCode,
                    seriesCode,
                    NormalizeOptional(request.StandardNumber) ?? "(空)",
                    dn,
                    pn);
                return new StandardMatchResponse
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(seriesCode)
                        ? $"未找到法兰规范：标准号={NormalizeOptional(request.StandardNumber) ?? "(空)"}，DN={dn}，PN={pn}。"
                        : $"未找到法兰规范：系列={seriesCode}，DN={dn}，PN={pn}。",
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
                "查询法兰规范失败：FamilyCode={FamilyCode}, SeriesCode={SeriesCode}, DN={DN}, PN={PN}, DatabaseType={DatabaseType}",
                familyCode,
                seriesCode,
                dn,
                pn,
                databaseType);
            throw;
        }
    }

    /// <summary>
    /// 查询指定规范系列下的全部有效法兰规范内容。
    /// </summary>
    public async Task<IReadOnlyList<FlangeStandardRecordDto>> GetFlangeRecordsAsync(
        long seriesId,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0)
            throw new ArgumentException("规范系列 ID 必须大于 0。", nameof(seriesId));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM"
            ? $"{schema}.STANDARD_FLANGE_RECORDS"
            : "standard_flange_records";
        string parameter = databaseType == "DM" ? ":" : "@";
        string sql = $"""
            SELECT
                SERIES_ID AS SeriesId,
                ID AS RecordId,
                SOURCE_ROW_NUMBER AS SourceRowNumber,
                DN AS DN,
                DN_VALUE AS DNValue,
                PN AS PN,
                PIPE_OUTER_DIAMETER_I AS PipeOuterDiameterSeriesI,
                PIPE_OUTER_DIAMETER_II AS PipeOuterDiameterSeriesII,
                FLANGE_OUTER_DIAMETER AS FlangeOuterDiameter,
                BOLT_CIRCLE_DIAMETER AS BoltCircleDiameter,
                BOLT_HOLE_DIAMETER AS BoltHoleDiameter,
                BOLT_COUNT AS BoltCount,
                BOLT_SPECIFICATION AS BoltSpecification,
                BOLT_RAW_SUFFIX AS BoltRawSuffix,
                FLANGE_THICKNESS AS FlangeThickness,
                RAISED_FACE_HEIGHT AS RaisedFaceHeight,
                FLANGE_INNER_DIAMETER_I AS FlangeInnerDiameterSeriesI,
                FLANGE_INNER_DIAMETER_II AS FlangeInnerDiameterSeriesII,
                RAW_VALUES_JSON AS RawValuesJson,
                WARNINGS_JSON AS WarningsJson
            FROM {table}
            WHERE SERIES_ID={parameter}SeriesId AND IS_ACTIVE=1
            ORDER BY DN_VALUE, ID
            """;

        await using DbConnection connection = databaseType == "DM"
            ? new DmConnection(GetConnectionString("DM"))
            : new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        List<StandardMatchRow> rows = (await connection.QueryAsync<StandardMatchRow>(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).AsList();

        _logger.LogInformation(
            "规范内容查询完成：SeriesId={SeriesId}, RecordCount={RecordCount}, DatabaseType={DatabaseType}",
            seriesId, rows.Count, databaseType);

        return rows.Select(row => ToMatchData(row).Record).ToList();
    }

    /// <summary>
    /// 查询动态规范系列当前版本的原始字段行。
    /// </summary>
    public async Task<DynamicStandardContentResponse?> GetDynamicContentAsync(
        long seriesId,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0)
            throw new ArgumentException("规范系列 ID 必须大于 0。", nameof(seriesId));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string versionTable = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string rowTable = databaseType == "DM" ? $"{schema}.STANDARD_DYNAMIC_VERSION_ROWS" : "standard_dynamic_version_rows";
        string parameter = databaseType == "DM" ? ":" : "@";
        string sql = $"""
            SELECT v.SERIES_ID AS SeriesId, v.ID AS VersionId, v.VERSION_NO AS VersionNo, v.VERSION_LABEL AS VersionLabel,
                   r.ROW_NUMBER AS RowNumber, r.VALUES_JSON AS ValuesJson
            FROM {versionTable} v
            INNER JOIN {rowTable} r ON r.VERSION_ID = v.ID
            WHERE v.SERIES_ID={parameter}SeriesId AND v.IS_CURRENT=1 AND v.IS_DELETED=0
            ORDER BY r.ROW_NUMBER, r.ROW_ID
            """;

        await using DbConnection connection = databaseType == "DM"
            ? new DmConnection(GetConnectionString("DM"))
            : new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        List<DynamicContentRow> rows = (await connection.QueryAsync<DynamicContentRow>(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        if (rows.Count == 0)
            return null;

        return new DynamicStandardContentResponse
        {
            SeriesId = seriesId,
            VersionId = rows[0].VersionId,
            VersionNo = rows[0].VersionNo,
            VersionLabel = rows[0].VersionLabel,
            Rows = rows.Select(row => new DynamicStandardContentRowDto
            {
                RowNumber = row.RowNumber,
                Values = JsonSerializer.Deserialize<Dictionary<string, string>>(row.ValuesJson)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    public async Task<DynamicStandardContentResponse?> GetDynamicContentByVersionAsync(
        long versionId,
        CancellationToken cancellationToken = default)
    {
        if (versionId <= 0)
            throw new ArgumentException("规范版本 ID 必须大于 0。", nameof(versionId));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string versionTable = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string rowTable = databaseType == "DM" ? $"{schema}.STANDARD_DYNAMIC_VERSION_ROWS" : "standard_dynamic_version_rows";
        string parameter = databaseType == "DM" ? ":" : "@";
        string sql = $"""
        SELECT v.SERIES_ID AS SeriesId, v.ID AS VersionId, v.VERSION_NO AS VersionNo,
               v.VERSION_LABEL AS VersionLabel, r.ROW_NUMBER AS RowNumber,
               r.VALUES_JSON AS ValuesJson
        FROM {versionTable} v
        INNER JOIN {rowTable} r ON r.VERSION_ID = v.ID
        WHERE v.ID={parameter}VersionId AND v.IS_DELETED=0
        ORDER BY r.ROW_NUMBER, r.ROW_ID
        """;

        await using DbConnection connection = databaseType == "DM"
            ? new DmConnection(GetConnectionString("DM"))
            : new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        List<DynamicContentRow> rows = (await connection.QueryAsync<DynamicContentRow>(
            new CommandDefinition(sql, new { VersionId = versionId }, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        if (rows.Count == 0)
            return null;

        return new DynamicStandardContentResponse
        {
            SeriesId = rows[0].SeriesId,
            VersionId = rows[0].VersionId,
            VersionNo = rows[0].VersionNo,
            VersionLabel = rows[0].VersionLabel,
            Rows = rows.Select(row => new DynamicStandardContentRowDto
            {
                RowNumber = row.RowNumber,
                Values = JsonSerializer.Deserialize<Dictionary<string, string>>(row.ValuesJson)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    /// <summary>
    /// 动态规范内容查询结果行。
    /// </summary>
    private sealed class DynamicContentRow
    {
        /// <summary>
        /// 规范系列 ID。
        /// </summary>
        public long SeriesId { get; init; }
        /// <summary>
        /// 规范版本 ID。
        /// </summary>
        public long VersionId { get; init; }
        /// <summary>
        /// 规范版本号。
        /// </summary>
        public string VersionNo { get; init; } = string.Empty;
        /// <summary>
        /// 规范版本标签。
        /// </summary>
        public string VersionLabel { get; init; } = string.Empty;
        /// <summary>
        /// 行号。
        /// </summary>
        public int RowNumber { get; init; }
        /// <summary>
        /// 值的 JSON 表示。
        /// </summary>
        public string ValuesJson { get; init; } = "{}";
    }
    /// <summary>
    /// 使用 MySQL 查询指定规范系列下的唯一法兰规范记录。
    /// </summary>
    /// <param name="familyCode">规范系列的家族代码。</param>
    /// <param name="seriesCode">规范系列代码。</param>
    /// <param name="request">标准匹配请求对象。</param>
    /// <param name="dn">公称直径。</param>
    /// <param name="dnValue">公称直径值。</param>
    /// <param name="pn">公称压力。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回匹配的标准数据，如果未找到则返回 null。</returns>
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
  AND sfr.dn_value = @DNValue
  AND sfr.is_active = 1
";

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
        IEnumerable<StandardMatchRow> rows = await connection.QueryAsync<StandardMatchRow>(
            new CommandDefinition(sql, new
            {
                FamilyCode = familyCode,
                SeriesCode = NormalizeOptional(seriesCode),
                DNValue = dnValue
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        stopwatch.Stop();
        _logger.LogInformation(
            "MySQL 查询完成：是否命中={Matched}, 耗时Ms={ElapsedMilliseconds}",
            rows.Any(),
            stopwatch.ElapsedMilliseconds);

        return SelectBestStaticMatch(rows, request, pn);
    }

    /// <summary>
    /// 从当前动态规范版本的 JSON 行中匹配法兰记录。
    /// </summary>
    private async Task<StandardMatchData?> QueryDynamicAsync(
        string familyCode,
        string seriesCode,
        StandardMatchRequest request,
        string dn,
        int dnValue,
        string pn,
        string databaseType,
        CancellationToken cancellationToken)
    {
        string schema = GetSchemaName();
        string versionTable = databaseType == "DM"
            ? $"{schema}.STANDARD_DOCUMENT_VERSIONS"
            : "standard_document_versions";
        string rowTable = databaseType == "DM"
            ? $"{schema}.STANDARD_DYNAMIC_VERSION_ROWS"
            : "standard_dynamic_version_rows";
        string seriesTable = databaseType == "DM"
            ? $"{schema}.STANDARD_SERIES"
            : "standard_series";
        string familyTable = databaseType == "DM"
            ? $"{schema}.STANDARD_FAMILIES"
            : "standard_families";
        string parameter = databaseType == "DM" ? ":" : "@";
        string sql = $"""
            SELECT ss.ID AS SeriesId, sf.CODE AS FamilyCode, sf.NAME AS FamilyName,
                   ss.SERIES_CODE AS SeriesCode, ss.SERIES_NAME AS SeriesName,
                   ss.STANDARD_NUMBER AS StandardNumber, ss.TABLE_NUMBER AS TableNumber,
                   ss.PRESSURE_RATING AS PressureRating, ss.FLANGE_TYPE AS FlangeType,
                   ss.FACE_TYPE AS FaceType, r.ROW_NUMBER AS SourceRowNumber,
                   r.VALUES_JSON AS ValuesJson
            FROM {versionTable} v
            INNER JOIN {rowTable} r ON r.VERSION_ID = v.ID
            INNER JOIN {seriesTable} ss ON ss.ID = v.SERIES_ID AND ss.IS_ACTIVE = 1
            INNER JOIN {familyTable} sf ON sf.ID = ss.FAMILY_ID AND sf.IS_ACTIVE = 1
            WHERE sf.CODE = {parameter}FamilyCode
              AND v.IS_CURRENT = 1
              AND v.IS_DELETED = 0
            ORDER BY CASE WHEN ss.SERIES_CODE = {parameter}SeriesCode THEN 0 ELSE 1 END,
                     CASE WHEN ss.STANDARD_NUMBER = {parameter}StandardNumber THEN 0 ELSE 1 END,
                     r.ROW_NUMBER
            """;

        await using DbConnection connection = databaseType == "DM"
            ? new DmConnection(GetConnectionString("DM"))
            : new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        string requestedStandardForOrder = NormalizeStandardNumber(request.StandardNumber) ?? string.Empty;
        IEnumerable<DynamicMatchRow> rows = await connection.QueryAsync<DynamicMatchRow>(
            new CommandDefinition(sql, new
            {
                FamilyCode = familyCode,
                 SeriesCode = NormalizeOptional(seriesCode),
                StandardNumber = requestedStandardForOrder
            }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        int rowCount = 0;
        int dnMatchCount = 0;
        int pnMatchCount = 0;
        StandardMatchData? bestMatch = null;
        int bestScore = int.MinValue;
        string? requestedStandardNumber = NormalizeStandardNumber(request.StandardNumber);
        string? requestedTableNumber = NormalizeMatchOptional(request.TableNumber);
        string? requestedFlangeType = NormalizeMatchOptional(request.FlangeType);
        string? requestedFaceType = NormalizeMatchOptional(request.FaceType);
        string? requestedSeriesCode = NormalizeMatchOptional(request.SeriesCode);
        foreach (DynamicMatchRow row in rows)
        {
            rowCount++;
            Dictionary<string, string> values = DeserializeDictionary(row.ValuesJson);
            string rowDn = NormalizeDn(GetDynamicValue(values,
                "DN", "DNValue", "DN值", "公称通径", "公称直径"));
            string rowPn = NormalizePn(GetDynamicValue(values,
                "PN", "PNValue", "PN值", "公称压力", "压力等级"));
            if (DiameterEquals(rowDn, dn))
                dnMatchCount++;

            if (!DiameterEquals(rowDn, dn)
                || !PressureEquals(rowPn, pn))
            {
                continue;
            }
            pnMatchCount++;

            _logger.LogInformation(
                "动态规范 DN/PN 候选命中：SeriesId={SeriesId}, RowNumber={RowNumber}, RawDN={RawDN}, RawPN={RawPN}, NormalizedDN={NormalizedDN}, NormalizedPN={NormalizedPN}, Keys={Keys}",
                row.SeriesId,
                row.SourceRowNumber,
                GetDynamicValue(values, "DN", "DNValue", "DN值", "公称通径", "公称直径"),
                GetDynamicValue(values, "PN", "PNValue", "PN值", "公称压力", "压力等级"),
                rowDn,
                rowPn,
                string.Join(",", values.Keys));

            string rowStandardNumber = NormalizeStandardNumber(row.StandardNumber) ?? string.Empty;
            string valueStandardNumber = NormalizeStandardNumber(GetDynamicValue(values, "StandardNumber", "标准号", "STANDARD_NO")) ?? string.Empty;
            if (requestedStandardNumber != null
                && rowStandardNumber != requestedStandardNumber
                && valueStandardNumber != requestedStandardNumber)
            {
                continue;
            }

            string rowTableNumber = NormalizeMatchText(row.TableNumber);
            string valueTableNumber = NormalizeMatchText(GetDynamicValue(values, "TableNumber", "表号", "TABLE_NUMBER"));
            if (requestedTableNumber != null
                && rowTableNumber != requestedTableNumber
                && valueTableNumber != requestedTableNumber)
            {
                continue;
            }

            string rowFlangeType = NormalizeMatchText(row.FlangeType);
            string valueFlangeType = NormalizeMatchText(GetDynamicValue(values, "FlangeType", "法兰类型", "FLG_TYPE"));
            if (requestedFlangeType != null
                && rowFlangeType != requestedFlangeType
                && valueFlangeType != requestedFlangeType)
            {
                continue;
            }

            string rowFaceType = NormalizeMatchText(row.FaceType);
            string valueFaceType = NormalizeMatchText(GetDynamicValue(values, "FaceType", "密封面形式", "密封面型式", "FACE_TYPE"));
            if (requestedFaceType != null
                && rowFaceType != requestedFaceType
                && valueFaceType != requestedFaceType)
            {
                continue;
            }

            FlangeStandardRecordDto record = ToDynamicRecord(row, values, dn, dnValue, rowPn);
            int score = 0;
            if (requestedSeriesCode != null && NormalizeMatchText(row.SeriesCode) == requestedSeriesCode) score += 8;
            if (requestedStandardNumber != null && (rowStandardNumber == requestedStandardNumber || valueStandardNumber == requestedStandardNumber)) score += 4;
            if (requestedTableNumber != null && (rowTableNumber == requestedTableNumber || valueTableNumber == requestedTableNumber)) score += 2;
            if (requestedFlangeType != null && (rowFlangeType == requestedFlangeType || valueFlangeType == requestedFlangeType)) score += 1;
            if (requestedFaceType != null && (rowFaceType == requestedFaceType || valueFaceType == requestedFaceType)) score += 1;

            if (score < bestScore)
            {
                continue;
            }

            _logger.LogInformation(
                "动态规范数据库命中：SeriesId={SeriesId}, RowNumber={RowNumber}, DN={DN}, PN={PN}",
                row.SeriesId, row.SourceRowNumber, dn, rowPn);
            bestScore = score;
            bestMatch = new StandardMatchData(ToDynamicSeries(row), record);
        }

        _logger.LogInformation(
            bestMatch == null
                ? "动态规范数据库未命中：FamilyCode={FamilyCode}, RequestedSeriesCode={SeriesCode}, DN={DN}, PN={PN}, DynamicRowCount={RowCount}, DNMatchCount={DNMatchCount}, DNPNMatchCount={PNMatchCount}"
                : "动态规范数据库匹配完成：FamilyCode={FamilyCode}, RequestedSeriesCode={SeriesCode}, DN={DN}, PN={PN}, DynamicRowCount={RowCount}, DNMatchCount={DNMatchCount}, DNPNMatchCount={PNMatchCount}, BestScore={BestScore}",
            familyCode, seriesCode, dn, pn, rowCount, dnMatchCount, pnMatchCount, bestScore);
        return bestMatch;
    }
    /// <summary>
    /// 将动态规范行转换为标准系列数据。
    /// </summary>
    /// <param name="row">动态规范行。</param>
    /// <returns>标准系列数据。</returns>
    private static StandardSeriesData ToDynamicSeries(DynamicMatchRow row) => new()
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
    };
    /// <summary>
    /// 将动态规范行和其值字典转换为法兰规范记录数据。
    /// </summary>
    /// <param name="row">动态规范行。</param>
    /// <param name="values">值字典。</param>
    /// <param name="dn">公称直径。</param>
    /// <param name="dnValue">公称直径值。</param>
    /// <param name="pn">公称压力。</param>
    /// <returns>法兰规范记录数据。</returns>
    private static FlangeStandardRecordDto ToDynamicRecord(
        DynamicMatchRow row,
        Dictionary<string, string> values,
        string dn,
        int dnValue,
        string pn) => new()
        {
            Id = row.SeriesId * 1000000 + row.SourceRowNumber,
            SeriesId = row.SeriesId,
            SourceRowNumber = row.SourceRowNumber,
            DN = dn,
            DNValue = dnValue,
            PN = pn,
            PipeOuterDiameterSeriesI = ParseDecimal(GetDynamicValue(values, "PipeOuterDiameterSeriesI")),
            PipeOuterDiameterSeriesII = ParseDecimal(GetDynamicValue(values, "PipeOuterDiameterSeriesII")),
            FlangeOuterDiameter = ParseDecimal(GetDynamicValue(values, "FlangeOuterDiameter")),
            BoltCircleDiameter = ParseDecimal(GetDynamicValue(values, "BoltCircleDiameter")),
            BoltHoleDiameter = ParseDecimal(GetDynamicValue(values, "BoltHoleDiameter")),
            BoltCount = ParseInt(GetDynamicValue(values, "BoltCount")),
            BoltSpecification = GetDynamicValue(values, "BoltSpecification"),
            BoltRawSuffix = GetDynamicValue(values, "BoltRawSuffix"),
            FlangeThickness = ParseDecimal(GetDynamicValue(values, "FlangeThickness")),
            RaisedFaceHeight = ParseDecimal(GetDynamicValue(values, "RaisedFaceHeight")),
            FlangeInnerDiameterSeriesI = ParseDecimal(GetDynamicValue(values, "FlangeInnerDiameterSeriesI")),
            FlangeInnerDiameterSeriesII = ParseDecimal(GetDynamicValue(values, "FlangeInnerDiameterSeriesII")),
            RawValues = values
        };
    /// <summary>
    /// 从动态规范的值字典中获取指定名称的值，按顺序尝试多个名称，返回第一个非空值。
    /// </summary>
    /// <param name="values">值字典。</param>
    /// <param name="names">要获取的值的名称列表。</param>
    /// <returns>第一个非空值，如果没有找到则返回空字符串。</returns>
    private static string GetDynamicValue(Dictionary<string, string> values, params string[] names)
    {
        foreach (string name in names)
        {
            KeyValuePair<string, string> pair = values.FirstOrDefault(item =>
                string.Equals(NormalizeDynamicKey(item.Key), NormalizeDynamicKey(name), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(pair.Value))
                return pair.Value.Trim();
        }

        return string.Empty;
    }
    /// <summary>
    /// 规范化动态规范的键名，移除下划线和空格，并转换为大写。
    /// </summary>
    /// <param name="value">要规范化的键名。</param>
    /// <returns>规范化后的键名。</returns>
    private static string NormalizeDynamicKey(string value)
    {
        string source = (value ?? string.Empty).Trim();

        // 只移除由动态导入器追加的“下划线 + 列序号”，不能无条件删除末尾数字。
        // 例如 DN_1 应映射为 DN；而业务字段名 FIELD1 必须仍保留为 FIELD1。
        source = Regex.Replace(source, @"_\d+$", string.Empty, RegexOptions.CultureInvariant);

        return source
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant();
    }
    /// <summary>
    /// 判断两个公称压力字符串是否表示相同的数值，忽略前缀 "PN"。
    /// </summary>
    /// <param name="left">左侧公称压力字符串。</param>
    /// <param name="right">右侧公称压力字符串。</param>
    /// <returns>如果表示相同的数值则返回 true，否则返回 false。</returns>
    private static bool PressureEquals(string left, string right)
    {
        string normalizedLeft = NormalizeMatchText(left).Replace("PN", string.Empty, StringComparison.Ordinal);
        string normalizedRight = NormalizeMatchText(right).Replace("PN", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalizedLeft, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal leftValue)
            && decimal.TryParse(normalizedRight, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rightValue)
            && leftValue == rightValue;
    }
    /// <summary>
    /// 判断两个公称直径字符串是否表示相同的数值，忽略前缀 "DN"。
    /// </summary>
    /// <param name="left">左侧公称直径字符串。</param>
    /// <param name="right">右侧公称直径字符串。</param>
    /// <returns>如果表示相同的数值则返回 true，否则返回 false。</returns>
    private static bool DiameterEquals(string left, string right)
    {
        string normalizedLeft = NormalizeMatchText(left).Replace("DN", string.Empty, StringComparison.Ordinal);
        string normalizedRight = NormalizeMatchText(right).Replace("DN", string.Empty, StringComparison.Ordinal);
        return int.TryParse(normalizedLeft, NumberStyles.Integer, CultureInfo.InvariantCulture, out int leftValue)
            && int.TryParse(normalizedRight, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rightValue)
            && leftValue == rightValue;
    }
    /// <summary>
    /// 尝试将字符串解析为 decimal 类型，如果解析失败则返回 null。
    /// </summary>
    /// <param name="value">要解析的字符串值。</param>
    /// <returns>解析成功则返回 decimal 值，否则返回 null。</returns>
    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result)
            ? result
            : null;

    /// <summary>
    /// 尝试将字符串解析为 int 类型，如果解析失败则返回 null。
    /// </summary>
    /// <param name="value">要解析的字符串值。</param>
    /// <returns>解析成功则返回 int 值，否则返回 null。</returns>
    private static int? ParseInt(string value) =>
        int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : null;
    
    /// <summary>
    /// 表示动态匹配行的数据结构。
    /// </summary>
    private sealed class DynamicMatchRow
    {
        /// <summary>
        /// 规范系列 ID。
        /// </summary>
        public long SeriesId { get; init; }
        /// <summary>
        /// 规范系列的家族代码。
        /// </summary>
        public string FamilyCode { get; init; } = string.Empty;
        /// <summary>
        /// 规范系列的家族名称。
        /// </summary>
        public string FamilyName { get; init; } = string.Empty;
        /// <summary>
        /// 规范系列代码。
        /// </summary>
        public string SeriesCode { get; init; } = string.Empty;
        /// <summary>
        /// 规范系列名称。
        /// </summary>
        public string SeriesName { get; init; } = string.Empty;
        /// <summary>
        /// 标准编号。
        /// </summary>
        public string StandardNumber { get; init; } = string.Empty;
        /// <summary>
        /// 表编号。
        /// </summary>
        public string TableNumber { get; init; } = string.Empty;
        /// <summary>
        /// 公称压力等级。
        /// </summary>
        public string PressureRating { get; init; } = string.Empty;
        /// <summary>
        /// 法兰类型。
        /// </summary>
        public string? FlangeType { get; init; }
        /// <summary>
        /// 法兰面类型。
        /// </summary>
        public string? FaceType { get; init; }
        /// <summary>
        /// 源数据行号。
        /// </summary>
        public int SourceRowNumber { get; init; }
        /// <summary>
        /// 值的 JSON 表示。
        /// </summary>
        public string ValuesJson { get; init; } = "{}";
    }
    /// <summary>
    /// 使用 DM 查询指定规范系列下的唯一法兰规范记录。
    /// </summary>
    /// <param name="familyCode">规范系列的家族代码。</param>
    /// <param name="seriesCode">规范系列代码。</param>
    /// <param name="request">标准匹配请求对象。</param>
    /// <param name="dn">公称直径。</param>
    /// <param name="dnValue">公称直径值。</param>
    /// <param name="pn">公称压力。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回匹配的标准数据，如果未找到则返回 null。</returns>
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
  AND sfr.DN_VALUE = :DNValue
  AND sfr.IS_ACTIVE = 1
";

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
        IEnumerable<StandardMatchRow> rows = await connection.QueryAsync<StandardMatchRow>(
        new CommandDefinition(string.Format(CultureInfo.InvariantCulture, sqlTemplate, schema), new
        {
            FamilyCode = familyCode,
            SeriesCode = seriesCode,
            DNValue = dnValue
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        stopwatch.Stop();
        _logger.LogInformation(
            "DM 查询完成：是否命中={Matched}, 耗时Ms={ElapsedMilliseconds}",
            rows.Any(),
            stopwatch.ElapsedMilliseconds);

        return SelectBestStaticMatch(rows, request, pn);
    }

    private static Dictionary<string, string> ToCadAttributes(
        StandardSeriesData series,
        FlangeStandardRecordDto record,
        string selectedSeries)
    {
        decimal? flangeInnerDiameter = selectedSeries == "Ⅱ系列"
            ? record.FlangeInnerDiameterSeriesII
            : record.FlangeInnerDiameterSeriesI;
        string flangeType = string.IsNullOrWhiteSpace(series.FlangeType) ? "PL" : series.FlangeType.Trim();
        string faceType = string.IsNullOrWhiteSpace(series.FaceType) ? "RF" : series.FaceType.Trim();

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DN"] = record.DN,
            ["PN"] = record.PN,
            ["FLG_TYPE"] = flangeType,
            ["FACE_TYPE"] = faceType,
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
    /// <summary>
    /// 从静态规范记录中选择最佳匹配的标准数据。
    /// </summary>
    /// <param name="rows">静态规范记录集合</param>
    /// <param name="request">标准匹配请求</param>
    /// <param name="requestedPn">请求的压力等级</param>
    /// <returns>最佳匹配的标准数据，如果没有匹配则返回 null</returns>
    private StandardMatchData? SelectBestStaticMatch(
        IEnumerable<StandardMatchRow> rows,
        StandardMatchRequest request,
        string requestedPn)
    {
        string? standardNumber = NormalizeStandardNumber(request.StandardNumber);
        string? tableNumber = NormalizeMatchOptional(request.TableNumber);
        string? flangeType = NormalizeMatchOptional(request.FlangeType);
        string? faceType = NormalizeMatchOptional(request.FaceType);
        string? seriesCode = NormalizeMatchOptional(request.SeriesCode);

        StandardMatchRow? best = rows
            .Where(row => PressureEquals(row.PN, requestedPn))
            .Where(row => standardNumber == null || NormalizeStandardNumber(row.StandardNumber) == standardNumber)
            .Where(row => tableNumber == null || NormalizeMatchText(row.TableNumber) == tableNumber)
            .Where(row => flangeType == null || NormalizeMatchText(row.FlangeType) == flangeType)
            .Where(row => faceType == null || NormalizeMatchText(row.FaceType) == faceType)
            .OrderByDescending(row => seriesCode != null && NormalizeMatchText(row.SeriesCode) == seriesCode)
            .ThenByDescending(row => standardNumber != null && NormalizeStandardNumber(row.StandardNumber) == standardNumber)
            .ThenByDescending(row => tableNumber != null && NormalizeMatchText(row.TableNumber) == tableNumber)
            .ThenBy(row => row.SourceRowNumber)
            .FirstOrDefault();

        return best == null ? null : ToMatchData(best);
    }
    
    /// <summary>
    /// 将静态规范记录转换为标准匹配数据。
    /// </summary>
    /// <param name="row">静态规范记录</param>
    /// <returns>标准匹配数据</returns>
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
    
    /// <summary>
    /// 获取数据库类型。
    /// </summary>
    /// <returns>数据库类型字符串，例如 "MYSQL" 或 "DM"</returns>
    private string GetDatabaseType() =>
        (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    
    /// <summary>
    /// 获取数据库模式名称。
    /// </summary>
    /// <returns>数据库模式名称字符串</returns>
    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || !schema.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new InvalidOperationException("Database:Schema 配置无效。");
        }

        return schema.ToUpperInvariant();
    }
    
    /// <summary>
    /// 获取数据库连接字符串。
    /// </summary>
    /// <param name="databaseType">数据库类型字符串，例如 "MYSQL" 或 "DM"</param>
    /// <returns>数据库连接字符串</returns>
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
    
    /// <summary>
    /// 标准化代码字符串。
    /// </summary>
    /// <param name="value">要标准化的代码字符串</param>
    /// <returns>标准化后的代码字符串</returns>
    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    
    /// <summary>
    /// 标准化匹配文本字符串。
    /// </summary>
    /// <param name="value">要标准化的匹配文本字符串</param>
    /// <returns>标准化后的匹配文本字符串</returns>
    private static string NormalizeMatchText(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        normalized = normalized.Normalize(System.Text.NormalizationForm.FormKC);
        return new string(normalized
            .Where(character => !char.IsWhiteSpace(character))
            .Select(character => character switch
            {
                '／' => '/',
                '－' or '–' or '—' => '-',
                '．' => '.',
                '：' => ':',
                _ => character
            })
            .ToArray());
    }
    
    /// <summary>
    /// 标准化标准号字符串。
    /// </summary>
    /// <param name="value">要标准化的标准号字符串</param>
    /// <returns>标准化后的标准号字符串，如果为空则返回 null</returns>
    private static string? NormalizeStandardNumber(string? value)
    {
        string normalized = NormalizeMatchText(value)
            .Replace("-", "/", StringComparison.Ordinal)
            .Replace("GB/T", "GBT", StringComparison.OrdinalIgnoreCase)
            .Replace("GB-T", "GBT", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
    
    /// <summary>
    /// 标准化可选匹配文本字符串。
    /// </summary>
    /// <param name="value">要标准化的可选匹配文本字符串</param>
    /// <returns>标准化后的可选匹配文本字符串，如果为空则返回 null</returns>
    private static string? NormalizeMatchOptional(string? value)
    {
        string normalized = NormalizeMatchText(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
    
    /// <summary>
    /// 标准化压力等级字符串。
    /// </summary>
    /// <param name="value">要标准化的压力等级字符串</param>
    /// <returns>标准化后的压力等级字符串</returns>
    private static string NormalizePn(string? value)
    {
        string normalized = NormalizeMatchText(value).Replace("PN", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
            ? $"PN{number.ToString("0.####", CultureInfo.InvariantCulture)}"
            : NormalizeMatchText(value);
    }
    
    /// <summary>
    /// 标准化可选字符串。
    /// </summary>
    /// <param name="value">要标准化的可选字符串</param>
    /// <returns>标准化后的可选字符串，如果为空则返回 null</returns>
    private static string? NormalizeOptional(string? value)
    {
        string normalized = NormalizeCode(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
    
    /// <summary>
    /// 标准化公称直径字符串。
    /// </summary>
    /// <param name="value">要标准化的公称直径字符串</param>
    /// <returns>标准化后的公称直径字符串</returns>
    private static string NormalizeDn(string? value)
    {
        string normalized = NormalizeMatchText(value);
        if (normalized.StartsWith("DN", StringComparison.Ordinal))
        {
            return normalized;
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dnValue)
            ? $"DN{dnValue}"
            : normalized;
    }
    
    /// <summary>
    /// 解析公称直径字符串的数值部分。
    /// </summary>
    /// <param name="dn">公称直径字符串，例如 "DN10"</param>
    /// <returns>公称直径的数值部分</returns>
    /// <exception cref="ArgumentException">当公称直径格式无效时抛出异常</exception>
    private static int ParseDnValue(string dn)
    {
        if (!int.TryParse(dn[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new ArgumentException("DN 必须是 DN10、DN50 等有效格式。", nameof(dn));
        }

        return value;
    }
    
    /// <summary>
    /// 标准化系列字符串。
    /// </summary>
    /// <param name="value">要标准化的系列字符串</param>
    /// <returns>标准化后的系列字符串</returns>
    private static string NormalizeSeries(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Contains('Ⅱ') || normalized.Contains("II", StringComparison.OrdinalIgnoreCase)
            ? "Ⅱ系列"
            : "Ⅰ系列";
    }
    
    /// <summary>
    /// 格式化数字为字符串。
    /// </summary>
    /// <param name="value">要格式化的数字</param>
    /// <returns>格式化后的数字字符串，如果为空则返回空字符串</returns>
    private static string FormatNumber(decimal? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
    
    /// <summary>
    /// 反序列化字典。
    /// </summary>
    /// <param name="json">要反序列化的 JSON 字符串</param>
    /// <returns>反序列化后的字典，如果为空或无效则返回空字典</returns>
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
    
    /// <summary>
    /// 反序列化列表。
    /// </summary>
    /// <param name="json">要反序列化的 JSON 字符串</param>
    /// <returns>反序列化后的列表，如果为空或无效则返回空列表</returns>
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



    /// <summary>
    /// 表示从数据库查询到的标准匹配行数据。
    /// </summary>
    private sealed class StandardMatchRow
    {
        /// <summary>
        /// 获取或初始化系列 ID。
        /// </summary>
        public long SeriesId { get; init; }
        /// <summary>
        /// 获取或初始化家族代码。
        /// </summary>
        public string FamilyCode { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化家族名称。
        /// </summary>
        public string FamilyName { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化系列代码。
        /// </summary>
        public string SeriesCode { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化系列名称。
        /// </summary>
        public string SeriesName { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化标准编号。
        /// </summary>
        public string StandardNumber { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化表编号。
        /// </summary>
        public string TableNumber { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化压力等级。
        /// </summary>  
        public string PressureRating { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化法兰类型。
        /// </summary>
        public string? FlangeType { get; init; }
        /// <summary>
        /// 获取或初始化法兰面类型。
        /// </summary>
        public string? FaceType { get; init; }
        /// <summary>
        /// 获取或初始化记录 ID。
        /// </summary>
        public long RecordId { get; init; }
        /// <summary>
        /// 获取或初始化源行号。
        /// </summary>
        public int SourceRowNumber { get; init; }
        /// <summary>
        /// 获取或初始化公称直径。
        /// </summary>
        public string DN { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化公称直径值。
        /// </summary>
        public int DNValue { get; init; }
        /// <summary>
        /// 获取或初始化公称压力。
        /// </summary>
        public string PN { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化系列 I 管外径。
        /// </summary>
        public decimal? PipeOuterDiameterSeriesI { get; init; }
        /// <summary>
        /// 获取或初始化系列 II 管外径。
        /// </summary>  
        public decimal? PipeOuterDiameterSeriesII { get; init; }
        /// <summary>
        /// 获取或初始化法兰外径。
        /// </summary>
        public decimal? FlangeOuterDiameter { get; init; }
        /// <summary>
        /// 获取或初始化螺栓圆直径。
        /// </summary>
        public decimal? BoltCircleDiameter { get; init; }
        /// <summary>
        /// 获取或初始化螺栓孔直径。
        /// </summary>
        public decimal? BoltHoleDiameter { get; init; }
        /// <summary>
        /// 获取或初始化螺栓数量。
        /// </summary>
        public int? BoltCount { get; init; }
        /// <summary>
        /// 获取或初始化螺栓规格。
        /// </summary>
        public string? BoltSpecification { get; init; }
        /// <summary>
        /// 获取或初始化螺栓原始后缀。
        /// </summary>
        public string? BoltRawSuffix { get; init; }
        /// <summary>
        /// 获取或初始化法兰厚度。
        /// </summary>
        public decimal? FlangeThickness { get; init; }
        /// <summary>
        /// 获取或初始化凸面高度。
        /// </summary>
        public decimal? RaisedFaceHeight { get; init; }
        /// <summary>
        /// 获取或初始化系列 I 法兰内径。
        /// </summary>
        public decimal? FlangeInnerDiameterSeriesI { get; init; }
        /// <summary>
        /// 获取或初始化系列 II 法兰内径。
        /// </summary>
        public decimal? FlangeInnerDiameterSeriesII { get; init; }
        /// <summary>
        /// 获取或初始化原始值的 JSON 表示。
        /// </summary>
        public string? RawValuesJson { get; init; }
        /// <summary>
        /// 获取或初始化警告信息的 JSON 表示。
        /// </summary>
        public string? WarningsJson { get; init; }
    }
    /// <summary>
    /// 表示标准系列数据。
    /// </summary>
    private sealed class StandardSeriesData
    {
        /// <summary>
        /// 获取或初始化系列 ID。
        /// </summary>
        public long Id { get; init; }
        /// <summary>
        /// 获取或初始化系列代码。
        /// </summary>  
        public string FamilyCode { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化系列名称。
        /// </summary>
        public string FamilyName { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化系列代码。
        /// </summary>
        public string SeriesCode { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化系列名称。
        /// </summary>
        public string SeriesName { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化标准编号。
        /// </summary>
        public string StandardNumber { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化表编号。
        /// </summary>
        public string TableNumber { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化压力等级。
        /// </summary>
        public string PressureRating { get; init; } = string.Empty;
        /// <summary>
        /// 获取或初始化法兰类型。
        /// </summary>
        public string? FlangeType { get; init; }
        /// <summary>
        /// 获取或初始化法兰面类型。
        /// </summary>
        public string? FaceType { get; init; }
    }
    /// <summary>
    /// 表示标准匹配数据，包括系列数据和记录数据。
    /// </summary>
    /// <param name="Series">系列数据。</param>
    /// <param name="Record">记录数据。</param>
    private sealed record StandardMatchData(StandardSeriesData Series, FlangeStandardRecordDto Record);
}
