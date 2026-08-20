using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;

namespace GB_NewCadPlus_IV.UploadApi.Services;

public sealed class DepartmentQueryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DepartmentQueryService> _logger;

    public DepartmentQueryService(IConfiguration configuration, ILogger<DepartmentQueryService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DepartmentListResponse> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        string databaseType = GetDatabaseType();
        try
        {
            var departments = databaseType == "DM"
                ? await QueryDmAsync(cancellationToken).ConfigureAwait(false)
                : await QueryMySqlAsync(cancellationToken).ConfigureAwait(false);

            return new DepartmentListResponse
            {
                Success = true,
                Message = "部门查询成功",
                Departments = departments
            };
        }
        catch (Exception ex)
        {
            if (databaseType == "DM")
            {
                string connectionString = GetConnectionString("DM");
                _logger.LogError(
                    ex,
                    "查询部门失败。DatabaseType=DM, Server={Server}, Port={Port}, Schema={Schema}",
                    GetConnectionValue(connectionString, "Server"),
                    GetConnectionValue(connectionString, "Port"),
                    GetSchemaName());
            }
            else
            {
                _logger.LogError(ex, "查询部门失败。DatabaseType={DatabaseType}", databaseType);
            }
            throw;
        }
    }

    private async Task<IReadOnlyList<DepartmentDto>> QueryMySqlAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT d.id AS Id,
                   d.cad_category_id AS CadCategoryId,
                   d.name AS Name,
                   COALESCE(d.display_name, d.name) AS RealName,
                   COALESCE(d.display_name, d.name) AS DisplayName,
                   COALESCE(d.description, '') AS Description,
                   COALESCE(d.sort_order, 0) AS SortOrder,
                   d.manager_user_id AS ManagerUserId,
                   COALESCE(d.is_active, 1) AS IsActive,
                   (SELECT COUNT(1) FROM users u WHERE u.department_id = d.id) AS UserCount
             FROM departments d
             WHERE d.cad_category_id IS NULL
                OR EXISTS (SELECT 1 FROM cad_categories c WHERE c.id = d.cad_category_id)
            ORDER BY d.sort_order, d.id";

        await using var connection = new MySqlConnection(GetConnectionString("MySQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await connection.QueryAsync<DepartmentDto>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
    }

    private async Task<IReadOnlyList<DepartmentDto>> QueryDmAsync(CancellationToken cancellationToken)
    {
        string schema = GetSchemaName();
        string connectionString = GetConnectionString("DM");
        _logger.LogInformation(
            "开始连接达梦数据库。Server={Server}, Port={Port}, Schema={Schema}",
            GetConnectionValue(connectionString, "Server"),
            GetConnectionValue(connectionString, "Port"),
            schema);
        string sql = $@"
            SELECT d.ID AS Id,
                   d.CAD_CATEGORY_ID AS CadCategoryId,
                   d.NAME AS Name,
                   COALESCE(d.DISPLAY_NAME, d.NAME) AS RealName,
                   COALESCE(d.DISPLAY_NAME, d.NAME) AS DisplayName,
                   COALESCE(d.DESCRIPTION, '') AS Description,
                   COALESCE(d.SORT_ORDER, 0) AS SortOrder,
                   d.MANAGER_USER_ID AS ManagerUserId,
                   COALESCE(d.IS_ACTIVE, 1) AS IsActive,
                   (SELECT COUNT(1) FROM {schema}.USERS u WHERE u.DEPARTMENT_ID = d.ID) AS UserCount
             FROM {schema}.DEPARTMENTS d
             WHERE d.CAD_CATEGORY_ID IS NULL
                OR EXISTS (SELECT 1 FROM {schema}.CAD_CATEGORIES c WHERE c.ID = d.CAD_CATEGORY_ID)
            ORDER BY d.SORT_ORDER, d.ID";

        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await connection.QueryAsync<DepartmentDto>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
    }

    private static string GetConnectionValue(string connectionString, string key)
    {
        foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf('=');
            if (separator <= 0 || !part[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return part[(separator + 1)..].Trim();
        }

        return "未配置";
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";

    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || !schema.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new InvalidOperationException("Database:Schema 配置无效。");
        return schema.ToUpperInvariant();
    }

    private string GetConnectionString(string databaseType)
    {
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = (_configuration.GetConnectionString(databaseType == "MYSQL" ? "MySQL" : "DM") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        return connectionString;
    }
}
