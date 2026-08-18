using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Text.Json;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>从数据库读取启用模板，并按规范化 Excel 表头匹配模板。</summary>
public sealed class StandardTemplateQueryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StandardTemplateQueryService> _logger;

    public StandardTemplateQueryService(IConfiguration configuration, ILogger<StandardTemplateQueryService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StandardTemplateMatchResult> MatchAsync(IReadOnlyList<string> headers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _logger.LogInformation("动态预览步骤 1/5：开始匹配模板。HeaderCount={HeaderCount}", headers.Count);
        HashSet<string> normalizedHeaders = headers.Select(NormalizeHeader).Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<StandardTemplateDto> templates = await GetActiveTemplatesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("动态预览步骤 2/5：模板读取完成。TemplateCount={TemplateCount}", templates.Count);
        StandardTemplateDto? bestTemplate = null;
        Dictionary<string, StandardTemplateColumnDto>? bestMappings = null;
        var bestScore = -1;

        foreach (StandardTemplateDto template in templates)
        {
            var mappings = new Dictionary<string, StandardTemplateColumnDto>(StringComparer.OrdinalIgnoreCase);
            foreach (StandardTemplateColumnDto column in template.Columns)
            {
                foreach (string alias in column.HeaderAliases.Append(column.FieldCode).Append(column.FieldName))
                {
                    string normalizedAlias = NormalizeHeader(alias);
                    string? matchedHeader = headers.FirstOrDefault(header => string.Equals(NormalizeHeader(header), normalizedAlias, StringComparison.OrdinalIgnoreCase));
                    if (matchedHeader != null)
                    {
                        mappings[matchedHeader] = column;
                        break;
                    }
                }
            }

            if (template.Columns.Where(column => column.IsRequired).Any(column => !mappings.Values.Contains(column))) continue;
            int score = mappings.Count;
            if (score > bestScore)
            {
                bestScore = score;
                bestTemplate = template;
                bestMappings = mappings;
            }
        }

        _logger.LogInformation("动态预览步骤 3/5：模板匹配完成。Matched={Matched}, TemplateCode={TemplateCode}, MappingCount={MappingCount}", bestTemplate != null, bestTemplate?.TemplateCode ?? string.Empty, bestMappings?.Count ?? 0);
        return new StandardTemplateMatchResult { Template = bestTemplate, HeaderMappings = bestMappings ?? new Dictionary<string, StandardTemplateColumnDto>(StringComparer.OrdinalIgnoreCase) };
    }

    private async Task<IReadOnlyList<StandardTemplateDto>> GetActiveTemplatesAsync(CancellationToken cancellationToken)
    {
        string databaseType = NormalizeCode(_configuration["Database:Type"]) == "MYSQL" ? "MYSQL" : "DM";
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
        string templateTable = databaseType == "MYSQL" ? "standard_templates" : $"{schema}.STANDARD_TEMPLATES";
        string columnTable = databaseType == "MYSQL" ? "standard_template_columns" : $"{schema}.STANDARD_TEMPLATE_COLUMNS";
        string sql = databaseType == "DM"
            ? $"SELECT t.ID AS TemplateId,t.TEMPLATE_CODE AS TemplateCode,t.TEMPLATE_NAME AS TemplateName,t.FAMILY_CODE AS FamilyCode,t.FILE_TYPE AS FileType,t.TEMPLATE_VERSION AS Version,c.ID AS ColumnId,c.FIELD_CODE AS FieldCode,c.FIELD_NAME AS FieldName,c.DATA_TYPE AS DataType,c.UNIT AS Unit,c.IS_REQUIRED AS IsRequired,c.SORT_ORDER AS SortOrder,c.HEADER_ALIASES_JSON AS HeaderAliasesJson,c.VALIDATION_JSON AS ValidationJson FROM {templateTable} t INNER JOIN {columnTable} c ON c.TEMPLATE_ID=t.ID WHERE t.IS_ACTIVE=1 ORDER BY t.ID,c.SORT_ORDER,c.ID"
            : $"SELECT t.ID AS TemplateId,t.TEMPLATE_CODE AS TemplateCode,t.TEMPLATE_NAME AS TemplateName,t.FAMILY_CODE AS FamilyCode,t.FILE_TYPE AS FileType,t.VERSION AS Version,c.ID AS ColumnId,c.FIELD_CODE AS FieldCode,c.FIELD_NAME AS FieldName,c.DATA_TYPE AS DataType,COALESCE(c.UNIT,'') AS Unit,c.IS_REQUIRED AS IsRequired,c.SORT_ORDER AS SortOrder,COALESCE(c.HEADER_ALIASES_JSON,'[]') AS HeaderAliasesJson,COALESCE(c.VALIDATION_JSON,'{{}}') AS ValidationJson FROM {templateTable} t INNER JOIN {columnTable} c ON c.TEMPLATE_ID=t.ID WHERE t.IS_ACTIVE=1 ORDER BY t.ID,c.SORT_ORDER,c.ID";
        _logger.LogInformation("动态预览步骤 2/5：准备读取模板。DatabaseType={DatabaseType}, Schema={Schema}", databaseType, schema);
        await using DbConnection connection = databaseType == "MYSQL" ? new MySqlConnection(GetConnectionString(databaseType)) : new DmConnection(GetConnectionString(databaseType));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        List<TemplateRow> rows;
        try
        {
            _logger.LogInformation("动态预览步骤 2/5：开始执行模板 SQL。DatabaseType={DatabaseType}, Sql={Sql}", databaseType, sql);
            rows = (await connection.QueryAsync<TemplateRow>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态预览步骤 2/5：读取模板失败。TemplateTable={TemplateTable}, ColumnTable={ColumnTable}", templateTable, columnTable);
            throw;
        }
        return rows.GroupBy(row => row.TemplateId).Select(group => new StandardTemplateDto
        {
            Id = group.Key,
            TemplateCode = group.First().TemplateCode,
            TemplateName = group.First().TemplateName,
            FamilyCode = group.First().FamilyCode,
            FileType = group.First().FileType,
            Version = group.First().Version,
            Columns = group.Select(row => new StandardTemplateColumnDto
            {
                Id = row.ColumnId,
                TemplateId = row.TemplateId,
                FieldCode = row.FieldCode,
                FieldName = row.FieldName,
                DataType = row.DataType,
                 Unit = row.Unit ?? string.Empty,
                IsRequired = row.IsRequired != 0,
                SortOrder = row.SortOrder,
                 HeaderAliases = DeserializeAliases(row.HeaderAliasesJson),
                 ValidationJson = string.IsNullOrWhiteSpace(row.ValidationJson) ? "{}" : row.ValidationJson
            }).ToList()
        }).ToList();
    }

    private static List<string> DeserializeAliases(string? json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private string GetConnectionString(string type) => (_configuration["Database:ConnectionString"] ?? string.Empty).Trim() is { Length: > 0 } configured ? configured : (_configuration.GetConnectionString(type == "MYSQL" ? "MySQL" : "DM") ?? throw new InvalidOperationException($"缺少 {type} 数据库连接字符串配置。"));
    private static string NormalizeHeader(string? value) => NormalizeCode(value).Replace("_", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private sealed class TemplateRow
    {
        public long TemplateId { get; init; }
        public string TemplateCode { get; init; } = string.Empty;
        public string TemplateName { get; init; } = string.Empty;
        public string FamilyCode { get; init; } = string.Empty;
        public string FileType { get; init; } = string.Empty;
        public int Version { get; init; }
        public long ColumnId { get; init; }
        public string FieldCode { get; init; } = string.Empty;
        public string FieldName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string? Unit { get; init; }
        public int IsRequired { get; init; }
        public int SortOrder { get; init; }
        public string? HeaderAliasesJson { get; init; }
        public string? ValidationJson { get; init; }
    }
}
