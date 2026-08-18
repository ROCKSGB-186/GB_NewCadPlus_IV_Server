using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 动态规范导入批次保存服务。只保存模板化原始行，不直接写入具体部件业务表。
/// </summary>
public sealed class DynamicStandardImportService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DynamicStandardImportService> _logger;

    public DynamicStandardImportService(IConfiguration configuration, ILogger<DynamicStandardImportService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DynamicStandardImportCommitResponse> CommitAsync(
        DynamicStandardImportCommitRequest request,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        _logger.LogInformation("动态确认步骤 1/6：开始校验请求。BatchId={BatchId}, SeriesId={SeriesId}, RowCount={RowCount}, Operator={OperatorName}", request.BatchId, request.SeriesId, request.Rows?.Count ?? 0, operatorName);
        ValidateRequest(request, operatorName);
        IReadOnlyList<DynamicStandardPreviewRowDto> rows = request.Rows ?? throw new ArgumentException("动态导入不能没有数据行。", nameof(request));
        if (!request.AllowWarnings && rows.Any(row => row.Warnings.Count > 0))
            throw new ArgumentException("预览存在警告，必须明确允许警告后才能确认。", nameof(request));
        if (rows.Any(row => row.Errors.Count > 0))
            throw new ArgumentException("预览存在错误行，不能确认导入。", nameof(request));
        if (!string.Equals(request.UpdateStrategy, DynamicStandardUpdateStrategies.Replace, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.UpdateStrategy, DynamicStandardUpdateStrategies.Merge, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("动态规范更新策略只能是 REPLACE 或 MERGE。", nameof(request));
        if (string.Equals(request.UpdateStrategy, DynamicStandardUpdateStrategies.Merge, StringComparison.OrdinalIgnoreCase)
            && (request.UniqueKeyFields == null || request.UniqueKeyFields.Count == 0))
            throw new ArgumentException("融合更新必须至少选择一个唯一键字段。", nameof(request));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string batchTable = databaseType == "DM" ? $"{schema}.STANDARD_IMPORT_BATCHES" : "standard_import_batches";
        string rowTable = databaseType == "DM" ? $"{schema}.STANDARD_IMPORT_ROWS" : "standard_import_rows";
        string versionTable = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENT_VERSIONS" : "standard_document_versions";
        string versionRowTable = databaseType == "DM" ? $"{schema}.STANDARD_DYNAMIC_VERSION_ROWS" : "standard_dynamic_version_rows";
        string parameter = databaseType == "DM" ? ":" : "@";
        _logger.LogInformation("动态确认步骤 2/6：数据库连接准备完成。DatabaseType={DatabaseType}, Schema={Schema}, BatchId={BatchId}", databaseType, schema, request.BatchId);
        await using DbConnection connection = await OpenConnectionAsync(databaseType, cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            long seriesId = request.SeriesId > 0
                ? await ValidateExistingSeriesAsync(connection, transaction, databaseType, schema, parameter, request.SeriesId, cancellationToken).ConfigureAwait(false)
                : await EnsureBaseSeriesAsync(connection, transaction, databaseType, schema, parameter, request, operatorName, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("动态确认步骤 3/6：基础规范系列校验完成。BatchId={BatchId}, SeriesId={SeriesId}, CreatedWhenMissing={CreatedWhenMissing}", request.BatchId, seriesId, request.SeriesId <= 0);

            // 首次上传未匹配模板时，必须由管理员明确确认后才能保存模板。
            long? templateId = request.TemplateId;
            if (request.ConfirmTemplateCreation)
            {
                if (request.TemplateDraft == null)
                    throw new ArgumentException("确认创建模板时必须提供模板草稿。", nameof(request));

                templateId = await SaveTemplateDraftAsync(
                    connection,
                    transaction,
                    request.TemplateDraft,
                    operatorName,
                    cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("动态确认步骤 4/6：模板草稿已保存。TemplateId={TemplateId}, TemplateCode={TemplateCode}", templateId, request.TemplateDraft.TemplateCode);
            }

            int existingBatch = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM {batchTable} WHERE BATCH_ID={parameter}BatchId",
                new { request.BatchId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (existingBatch > 0)
                throw new InvalidOperationException("该动态导入批次已经提交，不能重复确认。");
            _logger.LogInformation("动态确认步骤 5/6：批次幂等性校验通过。BatchId={BatchId}", request.BatchId);

            if (string.Equals(request.UpdateStrategy, DynamicStandardUpdateStrategies.Merge, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("融合更新的行级差异确认尚未完成，请先使用“新版替换（保留旧版本）”。");

            long versionId = await CreateDynamicVersionAsync(
                connection,
                transaction,
                versionTable,
                parameter,
                seriesId,
                request,
                operatorName,
                cancellationToken).ConfigureAwait(false);

            DateTime now = DateTime.UtcNow;
            await connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {batchTable} (BATCH_ID,SERIES_ID,VERSION_ID,TEMPLATE_ID,FAMILY_CODE,STATUS,ROW_COUNT,ERROR_COUNT,WARNING_COUNT,UPDATE_STRATEGY,DIFFERENCE_JSON,SOURCE_FILE_NAME,SOURCE_FILE_SHA256,CREATED_BY,CREATED_AT,EXPIRES_AT) VALUES ({parameter}BatchId,{parameter}SeriesId,{parameter}VersionId,{parameter}TemplateId,{parameter}FamilyCode,'CONFIRMED',{parameter}RowCount,0,{parameter}WarningCount,{parameter}UpdateStrategy,{parameter}DifferenceJson,{parameter}SourceFileName,{parameter}SourceFileSha256,{parameter}CreatedBy,{parameter}CreatedAt,{parameter}ExpiresAt)",
                new
                {
                    request.BatchId,
                SeriesId = seriesId,
                    VersionId = versionId,
                    templateId,
                    FamilyCode = request.FamilyCode.Trim(),
                    RowCount = rows.Count,
                    WarningCount = rows.Sum(row => row.Warnings.Count),
                    UpdateStrategy = request.UpdateStrategy.Trim().ToUpperInvariant(),
                    DifferenceJson = JsonSerializer.Serialize(new { request.UniqueKeyFields, request.ConflictDecisions }),
                    SourceFileName = request.SourceFileName.Trim(),
                    SourceFileSha256 = request.SourceFileSha256.Trim(),
                    CreatedBy = operatorName.Trim(),
                    CreatedAt = now,
                    ExpiresAt = now.AddDays(7)
                }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            _logger.LogInformation("动态确认步骤 6/6：批次主记录已保存。BatchId={BatchId}, RowCount={RowCount}", request.BatchId, request.Rows.Count);

            long nextRowId = databaseType == "DM"
                ? await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ROW_ID),0)+1 FROM {rowTable}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                : 0;
            int saved = 0;
            foreach (DynamicStandardPreviewRowDto row in rows)
            {
                _logger.LogDebug("动态确认写入行：BatchId={BatchId}, VersionId={VersionId}, RowNumber={RowNumber}", request.BatchId, versionId, row.RowNumber);
                var valuesJson = JsonSerializer.Serialize(row.Values);
                var errorsJson = JsonSerializer.Serialize(row.Errors);
                var warningsJson = JsonSerializer.Serialize(row.Warnings);
                string rowIdExpression = databaseType == "DM" ? $"{parameter}RowId" : "NULL";
                await connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO {rowTable} (ROW_ID,BATCH_ID,ROW_NUMBER,VALUES_JSON,ERRORS_JSON,WARNINGS_JSON,CREATED_AT) VALUES ({rowIdExpression},{parameter}BatchId,{parameter}RowNumber,{parameter}ValuesJson,{parameter}ErrorsJson,{parameter}WarningsJson,{parameter}CreatedAt)",
                    new { RowId = nextRowId++, request.BatchId, RowNumber = row.RowNumber, ValuesJson = valuesJson, ErrorsJson = errorsJson, WarningsJson = warningsJson, CreatedAt = now }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                saved++;

                string versionRowIdExpression = databaseType == "DM" ? $"{parameter}VersionRowId" : "NULL";
                long versionRowId = databaseType == "DM"
                    ? await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ROW_ID),0)+1 FROM {versionRowTable}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                    : 0;
                await connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO {versionRowTable} (ROW_ID,VERSION_ID,ROW_NUMBER,UNIQUE_KEY_JSON,VALUES_JSON,SOURCE_BATCH_ID,CREATED_AT) VALUES ({versionRowIdExpression},{parameter}VersionId,{parameter}RowNumber,{parameter}UniqueKeyJson,{parameter}VersionValuesJson,{parameter}SourceBatchId,{parameter}VersionCreatedAt)",
                    new
                    {
                        VersionRowId = versionRowId,
                        VersionId = versionId,
                        RowNumber = row.RowNumber,
                        UniqueKeyJson = JsonSerializer.Serialize(BuildUniqueKey(row, request.UniqueKeyFields)),
                        VersionValuesJson = valuesJson,
                        SourceBatchId = request.BatchId,
                        VersionCreatedAt = now
                    }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("动态确认步骤 7/7：动态行保存并提交事务完成。BatchId={BatchId}, SavedRowCount={SavedRowCount}", request.BatchId, saved);
            _logger.LogInformation("动态规范导入批次已确认：BatchId={BatchId}, SeriesId={SeriesId}, Rows={Rows}, Operator={OperatorName}", request.BatchId, request.SeriesId, saved, operatorName);
            return new DynamicStandardImportCommitResponse { Success = true, Message = templateId.HasValue && request.ConfirmTemplateCreation ? "模板已保存，动态规范新版本已创建。" : "动态规范新版本已创建，旧版本仍保留。", BatchId = request.BatchId, SavedRowCount = saved, Status = "CONFIRMED" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态确认事务失败，准备回滚。BatchId={BatchId}, SeriesId={SeriesId}, ExceptionType={ExceptionType}, Message={Message}",
                request.BatchId, request.SeriesId, ex.GetType().FullName, ex.Message);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static Dictionary<string, string> BuildUniqueKey(
        DynamicStandardPreviewRowDto row,
        IReadOnlyList<string> uniqueKeyFields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string field in uniqueKeyFields ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(field)) continue;
            result[field] = row.Values.TryGetValue(field, out string? value) ? value : string.Empty;
        }
        return result;
    }

    private async Task<long> CreateDynamicVersionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string versionTable,
        string parameter,
        long seriesId,
        DynamicStandardImportCommitRequest request,
        string operatorName,
        CancellationToken cancellationToken)
    {
        string databaseType = GetDatabaseType();
        string seriesTable = databaseType == "DM" ? $"{GetSchemaName()}.STANDARD_SERIES" : "standard_series";
        DateTime now = DateTime.UtcNow;
        long versionId = databaseType == "DM"
            ? await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ID),0)+1 FROM {versionTable}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
            : 0;
        // 版本采用软删除，已删除版本仍可能受 SERIES_ID + VERSION_NO 唯一约束保护，
        // 因此不能只统计有效版本，否则删除 DYNAMIC-1 后再次导入会重新生成同名版本。
        int versionNumber = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1)+1 FROM {versionTable} WHERE SERIES_ID={parameter}SeriesId",
            new { SeriesId = seriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        string versionNo = $"DYNAMIC-{versionNumber}";
        _logger.LogInformation("动态版本号已生成：SeriesId={SeriesId}, VersionNo={VersionNo}, ExistingVersionCount={ExistingVersionCount}",
            seriesId, versionNo, versionNumber - 1);

        long? seriesExists = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"SELECT ID FROM {seriesTable} WHERE ID={parameter}SeriesId AND IS_ACTIVE=1",
            new { SeriesId = seriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (!seriesExists.HasValue) throw new KeyNotFoundException("目标规范系列不存在或已停用。");

        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {versionTable} SET IS_CURRENT=0,UPDATED_AT=CURRENT_TIMESTAMP WHERE SERIES_ID={parameter}SeriesId AND IS_DELETED=0",
            new { SeriesId = seriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        string insert = databaseType == "DM"
            ? $"INSERT INTO {versionTable}(ID,SERIES_ID,VERSION_NO,VERSION_LABEL,CHANGE_SUMMARY,SOURCE_TYPE,STATUS,IS_CURRENT,IS_DELETED,CREATED_BY,CREATED_AT,UPDATED_AT) VALUES({parameter}Id,{parameter}SeriesId,{parameter}VersionNo,{parameter}VersionLabel,{parameter}ChangeSummary,'DYNAMIC_IMPORT','ACTIVE',1,0,{parameter}OperatorName,{parameter}CreatedAt,{parameter}UpdatedAt)"
            : $"INSERT INTO {versionTable}(SERIES_ID,VERSION_NO,VERSION_LABEL,CHANGE_SUMMARY,SOURCE_TYPE,STATUS,IS_CURRENT,IS_DELETED,CREATED_BY,CREATED_AT,UPDATED_AT) VALUES({parameter}SeriesId,{parameter}VersionNo,{parameter}VersionLabel,{parameter}ChangeSummary,'DYNAMIC_IMPORT','ACTIVE',1,0,{parameter}OperatorName,{parameter}CreatedAt,{parameter}UpdatedAt)";
        string versionLabel = BuildDynamicVersionLabel(request.SourceFileName);
        _logger.LogInformation("动态版本显示名称已规范化：SourceFileName={SourceFileName}, VersionLabel={VersionLabel}",
            request.SourceFileName, versionLabel);
        await connection.ExecuteAsync(new CommandDefinition(insert, new
        {
            Id = versionId,
                SeriesId = seriesId,
            VersionNo = versionNo,
            VersionLabel = versionLabel,
            ChangeSummary = $"动态 Excel 导入，策略={request.UpdateStrategy}",
            OperatorName = operatorName.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (databaseType != "DM")
            versionId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return versionId;
    }

    private async Task<long> ValidateExistingSeriesAsync(
        DbConnection connection,
        DbTransaction transaction,
        string databaseType,
        string schema,
        string parameter,
        long seriesId,
        CancellationToken cancellationToken)
    {
        string table = databaseType == "DM" ? $"{schema}.STANDARD_SERIES" : "standard_series";
        long? existing = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"SELECT ID FROM {table} WHERE ID={parameter}SeriesId AND IS_ACTIVE=1",
            new { SeriesId = seriesId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return existing ?? throw new KeyNotFoundException("目标规范系列不存在或已停用。");
    }

    private async Task<long> EnsureBaseSeriesAsync(
        DbConnection connection,
        DbTransaction transaction,
        string databaseType,
        string schema,
        string parameter,
        DynamicStandardImportCommitRequest request,
        string operatorName,
        CancellationToken cancellationToken)
    {
        string familyCode = request.FamilyCode.Trim().ToUpperInvariant();
        string seriesCode = request.SeriesCode.Trim().ToUpperInvariant();
        string standardNumber = string.IsNullOrWhiteSpace(request.BaseStandardNumber)
            ? request.StandardNumber.Trim()
            : request.BaseStandardNumber.Trim();
        string seriesName = request.SeriesName.Trim();
        string tableNumber = request.TableNumber.Trim();
        string pressureRating = request.PressureRating.Trim();
        if (string.IsNullOrWhiteSpace(familyCode) || string.IsNullOrWhiteSpace(seriesCode)
            || string.IsNullOrWhiteSpace(standardNumber) || string.IsNullOrWhiteSpace(seriesName))
            throw new ArgumentException("新建细分规范必须提供专业编码、系列编码、系列名称和基础规范号。", nameof(request));

        string familyTable = databaseType == "DM" ? $"{schema}.STANDARD_FAMILIES" : "standard_families";
        string seriesTable = databaseType == "DM" ? $"{schema}.STANDARD_SERIES" : "standard_series";
        long? familyId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"SELECT ID FROM {familyTable} WHERE CODE={parameter}FamilyCode AND IS_ACTIVE=1",
            new { FamilyCode = familyCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (!familyId.HasValue)
        {
            if (databaseType == "DM")
            {
                long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ID),0)+1 FROM {familyTable}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                await connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO {familyTable}(ID,CODE,NAME,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES({parameter}Id,{parameter}Code,{parameter}Name,1,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)",
                    new { Id = id, Code = familyCode, Name = familyCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                familyId = id;
            }
            else
            {
                familyId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    $"INSERT INTO {familyTable}(CODE,NAME,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES({parameter}Code,{parameter}Name,1,NOW(),NOW()); SELECT LAST_INSERT_ID();",
                    new { Code = familyCode, Name = familyCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }

        string documentTable = databaseType == "DM" ? $"{schema}.STANDARD_DOCUMENTS" : "standard_documents";
        long? documentId = request.StandardDocumentId;
        if (documentId.HasValue && documentId.Value > 0)
        {
            documentId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT ID FROM {documentTable} WHERE ID={parameter}DocumentId AND FAMILY_ID={parameter}FamilyId AND IS_ACTIVE=1",
                new { DocumentId = documentId.Value, FamilyId = familyId.Value }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        if (!documentId.HasValue || documentId.Value <= 0)
        {
            documentId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT ID FROM {documentTable} WHERE FAMILY_ID={parameter}FamilyId AND STANDARD_NUMBER={parameter}StandardNumber AND IS_ACTIVE=1",
                new { FamilyId = familyId.Value, StandardNumber = standardNumber }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        if (!documentId.HasValue)
        {
            string documentName = string.IsNullOrWhiteSpace(request.BaseStandardName) ? standardNumber : request.BaseStandardName.Trim();
            if (databaseType == "DM")
            {
                long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ID),0)+1 FROM {documentTable}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                await connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO {documentTable}(ID,FAMILY_ID,CATEGORY_ID,STANDARD_NUMBER,STANDARD_NAME,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES({parameter}Id,{parameter}FamilyId,{parameter}CategoryId,{parameter}StandardNumber,{parameter}StandardName,1,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)",
                    new { Id = id, FamilyId = familyId.Value, request.CategoryId, StandardNumber = standardNumber, StandardName = documentName }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                documentId = id;
            }
            else
            {
                documentId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    $"INSERT INTO {documentTable}(FAMILY_ID,CATEGORY_ID,STANDARD_NUMBER,STANDARD_NAME,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES({parameter}FamilyId,{parameter}CategoryId,{parameter}StandardNumber,{parameter}StandardName,1,NOW(),NOW()); SELECT LAST_INSERT_ID();",
                    new { FamilyId = familyId.Value, request.CategoryId, StandardNumber = standardNumber, StandardName = documentName }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }

        long? seriesId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"SELECT ID FROM {seriesTable} WHERE STANDARD_DOCUMENT_ID={parameter}DocumentId AND SERIES_CODE={parameter}SeriesCode AND SERIES_NAME={parameter}SeriesName AND TABLE_NUMBER={parameter}TableNumber AND PRESSURE_RATING={parameter}PressureRating AND IS_ACTIVE=1",
            new { DocumentId = documentId.Value, SeriesCode = seriesCode, SeriesName = seriesName, TableNumber = tableNumber, PressureRating = pressureRating }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (seriesId.HasValue)
        {
            if (request.CategoryId.HasValue)
                await connection.ExecuteAsync(new CommandDefinition($"UPDATE {seriesTable} SET CATEGORY_ID={parameter}CategoryId,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID={parameter}Id", new { Id = seriesId.Value, request.CategoryId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return seriesId.Value;
        }

        if (databaseType == "DM")
        {
            long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ID),0)+1 FROM {seriesTable}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO {seriesTable}(ID,FAMILY_ID,STANDARD_DOCUMENT_ID,CATEGORY_ID,SERIES_CODE,SERIES_NAME,STANDARD_NUMBER,TABLE_NUMBER,PRESSURE_RATING,FLANGE_TYPE,FACE_TYPE,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES({parameter}Id,{parameter}FamilyId,{parameter}DocumentId,{parameter}CategoryId,{parameter}SeriesCode,{parameter}SeriesName,{parameter}StandardNumber,{parameter}TableNumber,{parameter}PressureRating,NULL,NULL,1,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)",
                new { Id = id, FamilyId = familyId.Value, DocumentId = documentId.Value, request.CategoryId, SeriesCode = seriesCode, SeriesName = seriesName, StandardNumber = standardNumber, TableNumber = tableNumber, PressureRating = pressureRating }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return id;
        }

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"INSERT INTO {seriesTable}(FAMILY_ID,STANDARD_DOCUMENT_ID,CATEGORY_ID,SERIES_CODE,SERIES_NAME,STANDARD_NUMBER,TABLE_NUMBER,PRESSURE_RATING,FLANGE_TYPE,FACE_TYPE,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES({parameter}FamilyId,{parameter}DocumentId,{parameter}CategoryId,{parameter}SeriesCode,{parameter}SeriesName,{parameter}StandardNumber,{parameter}TableNumber,{parameter}PressureRating,NULL,NULL,1,NOW(),NOW()); SELECT LAST_INSERT_ID();",
            new { FamilyId = familyId.Value, DocumentId = documentId.Value, request.CategoryId, SeriesCode = seriesCode, SeriesName = seriesName, StandardNumber = standardNumber, TableNumber = tableNumber, PressureRating = pressureRating }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// 从上传文件名提取稳定的动态细分名称，例如“表50_PN2.5”。
    /// </summary>
    private static string BuildDynamicVersionLabel(string sourceFileName)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourceFileName ?? string.Empty);
        Match match = Regex.Match(
            fileName,
            @"(?<table>表\s*[0-9０-９]+)\s*(?:[_\-－—、，,：: ]\s*)?(?<pn>PN\s*[0-9０-９]+(?:[.．][0-9０-９]+)?)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return fileName;

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

    /// <summary>
    /// 在当前事务中保存首次上传生成的模板及字段定义。
    /// </summary>
    private async Task<long> SaveTemplateDraftAsync(
        DbConnection connection,
        DbTransaction transaction,
        StandardTemplateDraftDto draft,
        string operatorName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(draft.TemplateCode) || draft.Columns == null || draft.Columns.Count == 0)
            throw new ArgumentException("模板草稿必须包含模板编码和至少一个字段。", nameof(draft));

        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string templateTable = databaseType == "DM" ? $"{schema}.STANDARD_TEMPLATES" : "standard_templates";
        string columnTable = databaseType == "DM" ? $"{schema}.STANDARD_TEMPLATE_COLUMNS" : "standard_template_columns";
        string parameter = databaseType == "DM" ? ":" : "@";
        DateTime now = DateTime.UtcNow;
        long templateId;
        int templateVersion = 1;

        if (databaseType == "DM")
        {
            templateId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COALESCE(MAX(ID),0)+1 FROM {templateTable}",
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            templateVersion = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COALESCE(MAX(TEMPLATE_VERSION),0)+1 FROM {templateTable} WHERE TEMPLATE_CODE={parameter}TemplateCode",
                new { draft.TemplateCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            templateVersion = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COALESCE(MAX(VERSION),0)+1 FROM {templateTable} WHERE TEMPLATE_CODE={parameter}TemplateCode",
                new { draft.TemplateCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            templateId = 0;
        }

        string insertTemplateSql = databaseType == "DM"
            ? $"INSERT INTO {templateTable} (ID,TEMPLATE_CODE,TEMPLATE_NAME,FAMILY_CODE,FILE_TYPE,TEMPLATE_VERSION,IS_ACTIVE,DESCRIPTION,CREATED_BY,CREATED_AT,UPDATED_AT) VALUES ({parameter}Id,{parameter}TemplateCode,{parameter}TemplateName,{parameter}FamilyCode,{parameter}FileType,{parameter}TemplateVersion,1,{parameter}Description,{parameter}CreatedBy,{parameter}CreatedAt,{parameter}UpdatedAt)"
            : $"INSERT INTO {templateTable} (TEMPLATE_CODE,TEMPLATE_NAME,FAMILY_CODE,FILE_TYPE,VERSION,IS_ACTIVE,DESCRIPTION,CREATED_BY,CREATED_AT,UPDATED_AT) VALUES ({parameter}TemplateCode,{parameter}TemplateName,{parameter}FamilyCode,{parameter}FileType,{parameter}TemplateVersion,1,{parameter}Description,{parameter}CreatedBy,{parameter}CreatedAt,{parameter}UpdatedAt); SELECT LAST_INSERT_ID();";

        var templateParameters = new
        {
            Id = templateId,
            TemplateCode = draft.TemplateCode.Trim(),
            TemplateName = string.IsNullOrWhiteSpace(draft.TemplateName) ? draft.TemplateCode.Trim() : draft.TemplateName.Trim(),
            FamilyCode = (draft.FamilyCode ?? string.Empty).Trim(),
            FileType = string.IsNullOrWhiteSpace(draft.FileType) ? "XLSX" : draft.FileType.Trim().ToUpperInvariant(),
            TemplateVersion = templateVersion,
            Description = "由首次上传 Excel 自动生成，经管理员确认保存。",
            CreatedBy = operatorName.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        if (databaseType == "DM")
        {
            await connection.ExecuteAsync(new CommandDefinition(insertTemplateSql, templateParameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            templateId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(insertTemplateSql, templateParameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        int sortOrder = 0;
        foreach (StandardTemplateDraftColumnDto column in draft.Columns)
        {
            string fieldCode = string.IsNullOrWhiteSpace(column.FieldCode) ? $"FIELD_{sortOrder + 1}" : column.FieldCode.Trim();
            string fieldName = string.IsNullOrWhiteSpace(column.FieldName) ? column.Header.Trim() : column.FieldName.Trim();
            string aliasesJson = JsonSerializer.Serialize(new[] { column.Header.Trim() });
            string validationJson = "{}";
            string insertColumnSql = $"INSERT INTO {columnTable} (ID,TEMPLATE_ID,FIELD_CODE,FIELD_NAME,DATA_TYPE,UNIT,IS_REQUIRED,SORT_ORDER,HEADER_ALIASES_JSON,VALIDATION_JSON) VALUES ({parameter}Id,{parameter}TemplateId,{parameter}FieldCode,{parameter}FieldName,{parameter}DataType,{parameter}Unit,0,{parameter}SortOrder,{parameter}HeaderAliasesJson,{parameter}ValidationJson)";
            long columnId = databaseType == "DM"
                ? await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ID),0)+1 FROM {columnTable}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                : 0;
            if (databaseType == "DM")
            {
                await connection.ExecuteAsync(new CommandDefinition(insertColumnSql, new { Id = columnId, TemplateId = templateId, FieldCode = fieldCode, FieldName = fieldName, DataType = "TEXT", Unit = string.Empty, SortOrder = sortOrder, HeaderAliasesJson = aliasesJson, ValidationJson = validationJson }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            else
            {
                string mysqlSql = $"INSERT INTO {columnTable} (TEMPLATE_ID,FIELD_CODE,FIELD_NAME,DATA_TYPE,UNIT,IS_REQUIRED,SORT_ORDER,HEADER_ALIASES_JSON,VALIDATION_JSON) VALUES ({parameter}TemplateId,{parameter}FieldCode,{parameter}FieldName,'TEXT','',0,{parameter}SortOrder,{parameter}HeaderAliasesJson,{parameter}ValidationJson)";
                await connection.ExecuteAsync(new CommandDefinition(mysqlSql, new { TemplateId = templateId, FieldCode = fieldCode, FieldName = fieldName, SortOrder = sortOrder, HeaderAliasesJson = aliasesJson, ValidationJson = validationJson }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            sortOrder++;
        }

        return templateId;
    }
    private static void ValidateRequest(DynamicStandardImportCommitRequest request, string operatorName)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.BatchId)) throw new ArgumentException("动态导入批次号不能为空。", nameof(request));
        if (request.SeriesId <= 0 && (string.IsNullOrWhiteSpace(request.SeriesCode)
            || (string.IsNullOrWhiteSpace(request.BaseStandardNumber) && string.IsNullOrWhiteSpace(request.StandardNumber))
            || string.IsNullOrWhiteSpace(request.SeriesName)))
            throw new ArgumentException("未选择已有基础规范时，必须提供基础规范号、细分规范名称和系列编码。", nameof(request));
        if (request.Rows == null || request.Rows.Count == 0) throw new ArgumentException("动态导入不能没有数据行。", nameof(request));
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));
    }

    private async Task<DbConnection> OpenConnectionAsync(string databaseType, CancellationToken cancellationToken)
    {
        DbConnection connection = databaseType == "DM" ? new DmConnection(GetConnectionString("DM")) : new MySqlConnection(GetConnectionString("MYSQL"));
        try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private string GetSchemaName() => (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
    private string GetConnectionString(string type) => !string.IsNullOrWhiteSpace(_configuration["Database:ConnectionString"]) ? _configuration["Database:ConnectionString"]! : _configuration.GetConnectionString(type == "MYSQL" ? "MySQL" : "DM") ?? throw new InvalidOperationException($"缺少 {type} 数据库连接字符串配置。");
}
