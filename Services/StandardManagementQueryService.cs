using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 规范资料管理查询服务。
/// 只负责读取目录、系列和版本元数据，不负责写入文件或数据库。
/// </summary>
public sealed class StandardManagementQueryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StandardManagementQueryService> _logger;

    public StandardManagementQueryService(
        IConfiguration configuration,
        ILogger<StandardManagementQueryService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询专业/类别目录和规范系列，供客户端构造左侧树。
    /// </summary>
    public async Task<StandardManagementTreeResponse> GetTreeAsync(
        CancellationToken cancellationToken = default)
    {
        string databaseType = GetDatabaseType();
        try
        {
            (List<StandardManagementCategoryDto> categories,
                List<StandardDocumentDto> documents,
                List<StandardManagementSeriesDto> series) result = databaseType == "DM"
                ? await QueryDmTreeAsync(cancellationToken).ConfigureAwait(false)
                : await QueryMySqlTreeAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "规范管理目录查询完成：DatabaseType={DatabaseType}, CategoryCount={CategoryCount}, SeriesCount={SeriesCount}",
                databaseType,
                result.categories.Count,
                result.series.Count);

            return new StandardManagementTreeResponse
            {
                Success = true,
                Message = "规范目录查询成功。",
                Categories = result.categories,
                Documents = result.documents,
                Series = result.series
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "规范管理目录查询失败。DatabaseType={DatabaseType}", databaseType);
            throw;
        }
    }

    /// <summary>
    /// 按规范身份键精确定位规范系列。
    /// CategoryId 不参与匹配，因为它只表示目录中的展示位置。
    /// </summary>
    public async Task<StandardIdentityResolveResponse> ResolveIdentityAsync(
        StandardIdentityResolveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string familyCode = NormalizeIdentityValue(request.FamilyCode);
        string seriesCode = NormalizeIdentityValue(request.SeriesCode);
        string standardNumber = NormalizeIdentityValue(request.StandardNumber);
        string tableNumber = NormalizeIdentityValue(request.TableNumber);
        string pressureRating = NormalizeIdentityValue(request.PressureRating);

        if (string.IsNullOrWhiteSpace(familyCode)
            || string.IsNullOrWhiteSpace(seriesCode)
            || string.IsNullOrWhiteSpace(standardNumber))
        {
            throw new ArgumentException("规范身份至少需要 FamilyCode、SeriesCode 和 StandardNumber。", nameof(request));
        }

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string seriesTable = databaseType == "DM" ? $"{schema}.STANDARD_SERIES" : "standard_series";
        string parameterPrefix = databaseType == "DM" ? ":" : "@";
        string sql = $"""
            SELECT
                ID AS Id,
                CATEGORY_ID AS CategoryId,
                sf.CODE AS FamilyCode,
                SERIES_CODE AS SeriesCode,
                SERIES_NAME AS SeriesName,
                STANDARD_NUMBER AS StandardNumber,
                TABLE_NUMBER AS TableNumber,
                PRESSURE_RATING AS PressureRating,
                COALESCE(FLANGE_TYPE, '') AS FlangeType,
                COALESCE(FACE_TYPE, '') AS FaceType
            FROM {seriesTable} ss
            INNER JOIN {(databaseType == "DM" ? $"{schema}.STANDARD_FAMILIES" : "standard_families")} sf ON sf.ID = ss.FAMILY_ID
            WHERE ss.IS_ACTIVE = 1
              AND UPPER(TRIM(sf.CODE)) = {parameterPrefix}FamilyCode
              AND UPPER(TRIM(ss.SERIES_CODE)) = {parameterPrefix}SeriesCode
              AND UPPER(TRIM(ss.STANDARD_NUMBER)) = {parameterPrefix}StandardNumber
              AND UPPER(TRIM(COALESCE(ss.TABLE_NUMBER, ''))) = {parameterPrefix}TableNumber
              AND UPPER(TRIM(COALESCE(ss.PRESSURE_RATING, ''))) = {parameterPrefix}PressureRating
            ORDER BY ss.ID
            """;

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken)
            .ConfigureAwait(false);
        List<StandardManagementSeriesDto> matches = (await connection.QueryAsync<StandardManagementSeriesDto>(
            new CommandDefinition(
                sql,
                new { FamilyCode = familyCode, SeriesCode = seriesCode, StandardNumber = standardNumber, TableNumber = tableNumber, PressureRating = pressureRating },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false)).AsList();

        _logger.LogInformation(
            "规范身份定位完成：DatabaseType={DatabaseType}, FamilyCode={FamilyCode}, SeriesCode={SeriesCode}, StandardNumber={StandardNumber}, TableNumber={TableNumber}, PressureRating={PressureRating}, MatchCount={MatchCount}",
            databaseType, familyCode, seriesCode, standardNumber, tableNumber, pressureRating, matches.Count);

        if (matches.Count != 1)
        {
            return new StandardIdentityResolveResponse
            {
                Success = true,
                Exists = matches.Count > 0,
                MatchCount = matches.Count,
                IsUniqueMatch = false,
                Message = matches.Count == 0 ? "未找到对应的规范系列。" : "规范身份匹配到多个系列，请补充或修正身份字段。"
            };
        }

        StandardManagementSeriesDto series = matches[0];
        IReadOnlyList<StandardDocumentVersionDto> versions = await GetVersionsAsync(series.Id, cancellationToken)
            .ConfigureAwait(false);
        return new StandardIdentityResolveResponse
        {
            Success = true,
            Exists = true,
            MatchCount = 1,
            IsUniqueMatch = true,
            Message = "规范身份定位成功。",
            Series = series,
            CurrentVersion = versions.FirstOrDefault(version => version.IsCurrent)
        };
    }

    /// <summary>
    /// 将导入使用的系列模型转换为统一身份定位请求。
    /// </summary>
    public Task<StandardIdentityResolveResponse> ResolveIdentityAsync(
        StandardSeriesDto series,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);
        return ResolveIdentityAsync(new StandardIdentityResolveRequest
        {
            FamilyCode = series.FamilyCode,
            SeriesCode = series.SeriesCode,
            StandardNumber = series.StandardNumber,
            TableNumber = series.TableNumber,
            PressureRating = series.PressureRating
        }, cancellationToken);
    }

    /// <summary>
    /// 按关键词查询规范系列，并返回当前版本摘要。
    /// </summary>
    public async Task<StandardManagementSearchResponse> SearchAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        string normalizedKeyword = (keyword ?? string.Empty).Trim();

        StandardManagementTreeResponse tree = await GetTreeAsync(cancellationToken).ConfigureAwait(false);
        List<StandardManagementSeriesDto> matchedSeries = tree.Series
            .Where(item => string.IsNullOrWhiteSpace(normalizedKeyword)
                || Contains(item.SeriesCode, normalizedKeyword)
                || Contains(item.SeriesName, normalizedKeyword)
                || Contains(item.StandardNumber, normalizedKeyword)
                || Contains(item.TableNumber, normalizedKeyword)
                || Contains(item.PressureRating, normalizedKeyword))
            .ToList();

        List<StandardManagementSearchItemDto> allItems = new(matchedSeries.Count);
        foreach (StandardManagementSeriesDto series in matchedSeries)
        {
            IReadOnlyList<StandardDocumentVersionDto> versions = await GetVersionsAsync(series.Id, cancellationToken)
                .ConfigureAwait(false);
            allItems.Add(new StandardManagementSearchItemDto
            {
                Series = series,
                CurrentVersion = versions.FirstOrDefault(version => version.IsCurrent),
                VersionCount = versions.Count
            });
        }

        List<StandardManagementSearchItemDto> items = allItems
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new StandardManagementSearchResponse
        {
            Success = true,
            Message = "规范搜索成功。",
            Page = page,
            PageSize = pageSize,
            TotalCount = allItems.Count,
            Items = items
        };
    }

    /// <summary>
    /// 查询一个规范系列的全部有效版本。
    /// </summary>
    public async Task<IReadOnlyList<StandardDocumentVersionDto>> GetVersionsAsync(
        long seriesId,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0)
        {
            throw new ArgumentException("规范系列 ID 必须大于 0。", nameof(seriesId));
        }

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "DM"
            ? $"{schema}.STANDARD_DOCUMENT_VERSIONS"
            : "standard_document_versions";
        string parameterPrefix = databaseType == "DM" ? ":" : "@";
        string batchTable = databaseType == "DM"
            ? $"{schema}.STANDARD_IMPORT_BATCHES"
            : "standard_import_batches";
        string sql = $"""
            SELECT
                v.ID AS Id,
                v.SERIES_ID AS SeriesId,
                v.VERSION_NO AS VersionNo,
                v.VERSION_LABEL AS VersionLabel,
                v.CHANGE_SUMMARY AS ChangeSummary,
                v.SOURCE_TYPE AS SourceType,
                v.STATUS AS Status,
                v.IS_CURRENT AS IsCurrent,
                v.CREATED_AT AS CreatedAt,
                v.CREATED_BY AS CreatedBy,
                COALESCE(b.SOURCE_FILE_NAME, '') AS SourceFileName
            FROM {table} v
            LEFT JOIN {batchTable} b ON b.VERSION_ID = v.ID
            WHERE v.SERIES_ID = {parameterPrefix}SeriesId AND v.IS_DELETED = 0
            ORDER BY v.CREATED_AT DESC, v.ID DESC
            """;

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "开始查询规范版本：SeriesId={SeriesId}, DatabaseType={DatabaseType}, ParameterPrefix={ParameterPrefix}",
            seriesId, databaseType, parameterPrefix);
        List<StandardVersionQueryRow> queryRows = (await connection.QueryAsync<StandardVersionQueryRow>(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).ToList();
        List<StandardDocumentVersionDto> rows = queryRows.Select(row => new StandardDocumentVersionDto
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            VersionNo = row.VersionNo,
            // 优先依据批次原始文件名恢复“表号_PN”显示名，兼容历史错误标签。
            VersionLabel = NormalizeDynamicVersionLabel(row.VersionLabel, row.SourceFileName),
            ChangeSummary = row.ChangeSummary,
            SourceType = row.SourceType,
            Status = row.Status,
            IsCurrent = row.IsCurrent,
            CreatedAt = row.CreatedAt,
            CreatedBy = row.CreatedBy
        }).ToList();
        _logger.LogInformation("规范版本查询完成：SeriesId={SeriesId}, VersionCount={VersionCount}", seriesId, rows.Count());
        return rows;
    }

    private static string NormalizeDynamicVersionLabel(string? versionLabel, string? sourceFileName)
    {
        // 优先使用数据库中的 VERSION_LABEL，因为这里可能是管理员重命名后的最新名称。
        // 只有版本标签为空或无法解析时，才使用导入批次文件名兼容历史数据。
        string fileName = Path.GetFileNameWithoutExtension(versionLabel ?? string.Empty);
        Match match = Regex.Match(fileName,
            @"(?<table>表\s*[0-9０-９]+)\s*(?:[_\-－—、，,：: ]\s*)?(?<pn>PN\s*[0-9０-９]+(?:[.．][0-9０-９]+)?)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"{NormalizeDynamicPart(match.Groups["table"].Value)}_{NormalizeDynamicPart(match.Groups["pn"].Value).ToUpperInvariant()}";
        }

        // 历史版本可能没有规范的 VERSION_LABEL，此时再从原始文件名恢复显示名称。
        fileName = Path.GetFileNameWithoutExtension(sourceFileName ?? string.Empty);
        match = Regex.Match(fileName,
            @"(?<table>表\s*[0-9０-９]+)\s*(?:[_\-－—、，,：: ]\s*)?(?<pn>PN\s*[0-9０-９]+(?:[.．][0-9０-９]+)?)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return versionLabel?.Trim() ?? string.Empty;

        return $"{NormalizeDynamicPart(match.Groups["table"].Value)}_{NormalizeDynamicPart(match.Groups["pn"].Value).ToUpperInvariant()}";
    }

    private static string NormalizeDynamicPart(string value)
    {
        return (value ?? string.Empty)
            .Replace('０', '0').Replace('１', '1').Replace('２', '2').Replace('３', '3').Replace('４', '4')
            .Replace('５', '5').Replace('６', '6').Replace('７', '7').Replace('８', '8').Replace('９', '9')
            .Replace('．', '.')
            .Replace(" ", string.Empty)
            .Trim();
    }

    private sealed class StandardVersionQueryRow
    {
        public long Id { get; init; }
        public long SeriesId { get; init; }
        public string VersionNo { get; init; } = string.Empty;
        public string? VersionLabel { get; init; }
        public string ChangeSummary { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool IsCurrent { get; init; }
        public DateTime? CreatedAt { get; init; }
        public string CreatedBy { get; init; } = string.Empty;
        public string SourceFileName { get; init; } = string.Empty;
    }

    private async Task<(List<StandardManagementCategoryDto> categories, List<StandardDocumentDto> documents, List<StandardManagementSeriesDto> series)> QueryMySqlTreeAsync(
        CancellationToken cancellationToken)
    {
        const string categorySql = """
            SELECT id AS Id, parent_id AS ParentId, code AS Code, name AS Name,
                   description AS Description, sort_order AS SortOrder
            FROM standard_categories
            WHERE is_active = 1
            ORDER BY sort_order, id
            """;
        const string documentSql = """
             SELECT sd.id AS Id, sf.code AS FamilyCode, sd.category_id AS CategoryId,
                    sd.standard_number AS StandardNumber, COALESCE(sd.standard_name, '') AS StandardName,
                    sd.is_active AS IsActive
             FROM standard_documents sd
             INNER JOIN standard_families sf ON sf.id = sd.family_id AND sf.is_active = 1
             WHERE sd.is_active = 1
             ORDER BY sd.standard_number, sd.id
             """;
        const string seriesSql = """
             SELECT ss.id AS Id, ss.category_id AS CategoryId, ss.standard_document_id AS StandardDocumentId, sf.code AS FamilyCode, ss.series_code AS SeriesCode,
                   series_name AS SeriesName, standard_number AS StandardNumber,
                   table_number AS TableNumber, pressure_rating AS PressureRating,
                   COALESCE(flange_type, '') AS FlangeType,
                   COALESCE(face_type, '') AS FaceType
             FROM standard_series ss
             INNER JOIN standard_families sf ON sf.id = ss.family_id AND sf.is_active = 1
             WHERE ss.is_active = 1
             ORDER BY ss.series_name, ss.id
            """;

        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (
            (await connection.QueryAsync<StandardManagementCategoryDto>(
                new CommandDefinition(categorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList(),
            (await connection.QueryAsync<StandardDocumentDto>(
                new CommandDefinition(documentSql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList(),
            (await connection.QueryAsync<StandardManagementSeriesDto>(
                new CommandDefinition(seriesSql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList());
    }

    private async Task<(List<StandardManagementCategoryDto> categories, List<StandardDocumentDto> documents, List<StandardManagementSeriesDto> series)> QueryDmTreeAsync(
        CancellationToken cancellationToken)
    {
        string schema = GetSchemaName();
        string categorySql = $"""
            SELECT ID AS Id, PARENT_ID AS ParentId, CODE AS Code, NAME AS Name,
                   DESCRIPTION AS Description, SORT_ORDER AS SortOrder
            FROM {schema}.STANDARD_CATEGORIES
            WHERE IS_ACTIVE = 1
            ORDER BY SORT_ORDER, ID
            """;
        string documentSql = $"""
             SELECT sd.ID AS Id, sf.CODE AS FamilyCode, sd.CATEGORY_ID AS CategoryId,
                    sd.STANDARD_NUMBER AS StandardNumber, COALESCE(sd.STANDARD_NAME, '') AS StandardName,
                    sd.IS_ACTIVE AS IsActive
             FROM {schema}.STANDARD_DOCUMENTS sd
             INNER JOIN {schema}.STANDARD_FAMILIES sf ON sf.ID = sd.FAMILY_ID AND sf.IS_ACTIVE = 1
             WHERE sd.IS_ACTIVE = 1
             ORDER BY sd.STANDARD_NUMBER, sd.ID
             """;
        string seriesSql = $"""
             SELECT ss.ID AS Id, ss.CATEGORY_ID AS CategoryId, ss.STANDARD_DOCUMENT_ID AS StandardDocumentId, sf.CODE AS FamilyCode, ss.SERIES_CODE AS SeriesCode,
                   SERIES_NAME AS SeriesName, STANDARD_NUMBER AS StandardNumber,
                   TABLE_NUMBER AS TableNumber, PRESSURE_RATING AS PressureRating,
                   COALESCE(FLANGE_TYPE, '') AS FlangeType,
                   COALESCE(FACE_TYPE, '') AS FaceType
             FROM {schema}.STANDARD_SERIES ss
             INNER JOIN {schema}.STANDARD_FAMILIES sf ON sf.ID = ss.FAMILY_ID AND sf.IS_ACTIVE = 1
             WHERE ss.IS_ACTIVE = 1
             ORDER BY ss.SERIES_NAME, ss.ID
            """;

        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (
            (await connection.QueryAsync<StandardManagementCategoryDto>(
                new CommandDefinition(categorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList(),
            (await connection.QueryAsync<StandardDocumentDto>(
                new CommandDefinition(documentSql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList(),
            (await connection.QueryAsync<StandardManagementSeriesDto>(
                new CommandDefinition(seriesSql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList());
    }

    private async Task<DbConnection> OpenConnectionAsync(string databaseType, CancellationToken cancellationToken)
    {
        DbConnection connection = databaseType == "DM"
            ? new DmConnection(GetConnectionString("DM"))
            : new MySqlConnection(GetConnectionString("MYSQL"));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string NormalizeIdentityValue(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private string GetDatabaseType()
    {
        return (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL"
            ? "MYSQL"
            : "DM";
    }

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
            string key = databaseType == "MYSQL" ? "MySQL" : "DM";
            connectionString = (_configuration.GetConnectionString(key) ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        }

        return connectionString;
    }

    private static bool Contains(string value, string keyword)
    {
        return value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
