using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;

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
        string sql = $"""
            SELECT
                ID AS Id,
                SERIES_ID AS SeriesId,
                VERSION_NO AS VersionNo,
                VERSION_LABEL AS VersionLabel,
                CHANGE_SUMMARY AS ChangeSummary,
                SOURCE_TYPE AS SourceType,
                STATUS AS Status,
                IS_CURRENT AS IsCurrent,
                CREATED_AT AS CreatedAt,
                CREATED_BY AS CreatedBy
            FROM {table}
            WHERE SERIES_ID = @SeriesId AND IS_DELETED = 0
            ORDER BY CREATED_AT DESC, ID DESC
            """;

        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken)
            .ConfigureAwait(false);
        IEnumerable<StandardDocumentVersionDto> rows = await connection.QueryAsync<StandardDocumentVersionDto>(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return rows.ToList();
    }

    private async Task<(List<StandardManagementCategoryDto> categories, List<StandardManagementSeriesDto> series)> QueryMySqlTreeAsync(
        CancellationToken cancellationToken)
    {
        const string categorySql = """
            SELECT id AS Id, parent_id AS ParentId, code AS Code, name AS Name,
                   description AS Description, sort_order AS SortOrder
            FROM standard_categories
            WHERE is_active = 1
            ORDER BY sort_order, id
            """;
        const string seriesSql = """
            SELECT id AS Id, category_id AS CategoryId, series_code AS SeriesCode,
                   series_name AS SeriesName, standard_number AS StandardNumber,
                   table_number AS TableNumber, pressure_rating AS PressureRating,
                   COALESCE(flange_type, '') AS FlangeType,
                   COALESCE(face_type, '') AS FaceType
            FROM standard_series
            WHERE is_active = 1
            ORDER BY series_name, id
            """;

        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (
            (await connection.QueryAsync<StandardManagementCategoryDto>(
                new CommandDefinition(categorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList(),
            (await connection.QueryAsync<StandardManagementSeriesDto>(
                new CommandDefinition(seriesSql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList());
    }

    private async Task<(List<StandardManagementCategoryDto> categories, List<StandardManagementSeriesDto> series)> QueryDmTreeAsync(
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
        string seriesSql = $"""
            SELECT ID AS Id, CATEGORY_ID AS CategoryId, SERIES_CODE AS SeriesCode,
                   SERIES_NAME AS SeriesName, STANDARD_NUMBER AS StandardNumber,
                   TABLE_NUMBER AS TableNumber, PRESSURE_RATING AS PressureRating,
                   COALESCE(FLANGE_TYPE, '') AS FlangeType,
                   COALESCE(FACE_TYPE, '') AS FaceType
            FROM {schema}.STANDARD_SERIES
            WHERE IS_ACTIVE = 1
            ORDER BY SERIES_NAME, ID
            """;

        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (
            (await connection.QueryAsync<StandardManagementCategoryDto>(
                new CommandDefinition(categorySql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList(),
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
