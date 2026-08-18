using Dapper;
using Dm;
using GB_NewCadPlus_IV.UploadApi.Models;
using MySql.Data.MySqlClient;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 规范 Excel 导入服务。
/// 预览批次暂存在服务器内存中，确认后才通过事务写入规范表。
/// </summary>
public sealed class StandardImportService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StandardImportService> _logger;
    private readonly StandardManagementQueryService _standardManagementQueryService;
    private readonly ConcurrentDictionary<string, ImportBatch> _batches = new(StringComparer.Ordinal);

    /// <summary>
    /// 创建规范导入服务。
    /// </summary>
    public StandardImportService(
        IConfiguration configuration,
        ILogger<StandardImportService> logger,
        StandardManagementQueryService standardManagementQueryService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _standardManagementQueryService = standardManagementQueryService ?? throw new ArgumentNullException(nameof(standardManagementQueryService));
    }

    /// <summary>
    /// 解析 Excel 并返回预览结果，不写入数据库。
    /// </summary>
    public async Task<StandardImportPreviewResponse> PreviewAsync(
        Stream excelStream,
        StandardSeriesDto series,
        string sourceFileName = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(excelStream);
        ArgumentNullException.ThrowIfNull(series);

        if (excelStream.CanSeek)
        {
            excelStream.Position = 0;
        }

        List<StandardImportRowDto> rows;
        try
        {
            rows = await Task.Run(
                () => ParseWorkbook(excelStream, series),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "规范 Excel 文件解析失败。文件可能不是有效的 .xlsx 文件。");
            throw new InvalidDataException(
                "上传文件不是有效的 Excel .xlsx 文件，请使用 Excel 的“另存为 .xlsx”重新保存后再上传。",
                ex);
        }

        string batchId = Guid.NewGuid().ToString("N");
        var batch = new ImportBatch(batchId, series, rows, DateTime.UtcNow);
        _batches[batchId] = batch;

        int errorCount = rows.Sum(row => row.Errors.Count);
        int warningCount = rows.Sum(row => row.Warnings.Count);
        StandardIdentityResolveResponse identity = await _standardManagementQueryService
            .ResolveIdentityAsync(series, cancellationToken)
            .ConfigureAwait(false);
        StandardImportDuplicateSeriesDto? duplicateSeries = await FindDuplicateSeriesAsync(series, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "规范 Excel 预览完成。BatchId={BatchId}, FileName={FileName}, SeriesName={SeriesName}, StandardNumber={StandardNumber}, TableNumber={TableNumber}, PressureRating={PressureRating}, RowCount={RowCount}, ErrorCount={ErrorCount}, WarningCount={WarningCount}, DuplicateSeriesId={DuplicateSeriesId}",
            batchId,
            Path.GetFileName(sourceFileName ?? string.Empty),
            series.SeriesName,
            series.StandardNumber,
            series.TableNumber,
            series.PressureRating,
            rows.Count,
            errorCount,
            warningCount,
            duplicateSeries?.SeriesId);

        return new StandardImportPreviewResponse
        {
            Success = errorCount == 0,
            Message = errorCount == 0 ? "Excel 解析成功，请确认后导入。" : "Excel 存在错误，请修正后重新上传。",
            BatchId = batchId,
            SourceFileName = Path.GetFileName(sourceFileName ?? string.Empty),
            Series = series,
            DuplicateSeries = duplicateSeries,
            IdentityResult = ToIdentityResult(identity),
            Rows = rows,
            RowCount = rows.Count,
            ErrorCount = errorCount,
            WarningCount = warningCount
        };
    }

    /// <summary>
    /// 解析 JSON 规范文件并返回预览结果，不写入数据库。
    /// </summary>
    public async Task<StandardImportPreviewResponse> PreviewJsonAsync(
        Stream jsonStream,
        string sourceFileName = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonStream);

        if (jsonStream.CanSeek)
        {
            jsonStream.Position = 0;
        }

        StandardJsonImportDocumentDto document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<StandardJsonImportDocumentDto>(
                jsonStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("JSON 文件内容为空。");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "规范 JSON 文件解析失败。");
            throw new InvalidDataException("上传文件不是有效的规范 JSON 文件，请按照模板检查 JSON 格式。", ex);
        }

        StandardSeriesDto series = new()
        {
            FamilyCode = document.FamilyCode.Trim(),
            FamilyName = document.FamilyName.Trim(),
            SeriesCode = document.SeriesCode.Trim(),
            SeriesName = document.SeriesName.Trim(),
            StandardNumber = document.StandardNumber.Trim(),
            TableNumber = document.TableNumber.Trim(),
            PressureRating = document.PressureRating.Trim(),
            FlangeType = string.IsNullOrWhiteSpace(document.FlangeType) ? "PL" : document.FlangeType.Trim(),
            FaceType = string.IsNullOrWhiteSpace(document.FaceType) ? "RF" : document.FaceType.Trim()
        };

        ValidateJsonSeries(series);
        List<StandardImportRowDto> rows = ValidateJsonRows(document.Records, series.PressureRating);
        if (rows.Count == 0)
        {
            throw new InvalidDataException("JSON 文件的 Records 数组不能为空。");
        }

        string batchId = Guid.NewGuid().ToString("N");
        _batches[batchId] = new ImportBatch(batchId, series, rows, DateTime.UtcNow);

        int errorCount = rows.Sum(row => row.Errors.Count);
        int warningCount = rows.Sum(row => row.Warnings.Count);
        StandardIdentityResolveResponse identity = await _standardManagementQueryService
            .ResolveIdentityAsync(series, cancellationToken)
            .ConfigureAwait(false);
        StandardImportDuplicateSeriesDto? duplicateSeries = await FindDuplicateSeriesAsync(series, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "规范 JSON 预览完成。BatchId={BatchId}, FileName={FileName}, SeriesName={SeriesName}, StandardNumber={StandardNumber}, TableNumber={TableNumber}, PressureRating={PressureRating}, RowCount={RowCount}, ErrorCount={ErrorCount}, WarningCount={WarningCount}, DuplicateSeriesId={DuplicateSeriesId}",
            batchId,
            Path.GetFileName(sourceFileName ?? string.Empty),
            series.SeriesName,
            series.StandardNumber,
            series.TableNumber,
            series.PressureRating,
            rows.Count,
            errorCount,
            warningCount,
            duplicateSeries?.SeriesId);

        return new StandardImportPreviewResponse
        {
            Success = errorCount == 0,
            Message = errorCount == 0 ? "JSON 解析成功，请确认后导入。" : "JSON 存在错误，请修正后重新上传。",
            BatchId = batchId,
            SourceFileName = Path.GetFileName(sourceFileName ?? string.Empty),
            Series = series,
            DuplicateSeries = duplicateSeries,
            IdentityResult = ToIdentityResult(identity),
            Rows = rows,
            RowCount = rows.Count,
            ErrorCount = errorCount,
            WarningCount = warningCount
        };
    }

    /// <summary>
    /// 确认预览批次并以事务方式写入数据库。
    /// </summary>
    public async Task<StandardImportCommitResponse> CommitAsync(
        StandardImportCommitRequest request,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(operatorName))
            throw new ArgumentException("操作用户名不能为空。", nameof(operatorName));

        string normalizedBatchId = request.BatchId?.Trim() ?? string.Empty;
        if (!_batches.TryGetValue(normalizedBatchId, out ImportBatch? batch))
        {
            throw new KeyNotFoundException("导入批次不存在或已过期，请重新上传 Excel。");
        }

        if (batch.Rows.Any(row => !row.IsValid))
        {
            throw new InvalidOperationException("导入批次存在错误行，不能确认导入。");
        }

        if (!request.AllowWarnings && batch.Rows.Any(row => row.Warnings.Count > 0))
        {
            throw new InvalidOperationException("导入批次存在警告，请确认警告后再提交。");
        }

        StandardSeriesDto committedSeries = BuildCommittedSeries(batch.Series, request.Series);
        StandardImportDuplicateSeriesDto? duplicateSeries = await FindDuplicateSeriesAsync(committedSeries, cancellationToken).ConfigureAwait(false);
        if (duplicateSeries != null)
        {
            if (request.DuplicateStrategy == StandardImportDuplicateStrategy.NewImport)
            {
                throw new InvalidOperationException(
                    $"已存在相同规范：{duplicateSeries.SeriesName}-{duplicateSeries.StandardNumber}-{duplicateSeries.TableNumber}-{duplicateSeries.PressureRating}。请等待版本数据迁移完成后再选择创建新版本或覆盖当前数据。");
            }

            throw new InvalidOperationException(
                "当前系统尚未完成法兰规范记录的版本快照与恢复能力，暂不允许创建新版本或覆盖当前数据，以避免丢失历史规范内容。");
        }

        if (request.DuplicateStrategy != StandardImportDuplicateStrategy.NewImport)
        {
            throw new InvalidOperationException("未发现同名规范时只能选择“导入为新规范”。");
        }

        batch = batch with { Series = committedSeries };
        _batches[normalizedBatchId] = batch;

        string databaseType = GetDatabaseType();
        try
        {
            int imported = databaseType == "DM"
                ? await CommitDmAsync(batch, cancellationToken).ConfigureAwait(false)
                : await CommitMySqlAsync(batch, cancellationToken).ConfigureAwait(false);

            // 只有数据库事务成功后才删除批次，便于校验失败或警告确认失败后重试。
            _batches.TryRemove(normalizedBatchId, out _);

            _logger.LogInformation(
                "规范导入提交成功。BatchId={BatchId}, Operator={OperatorName}, Strategy={Strategy}, SeriesName={SeriesName}, StandardNumber={StandardNumber}, TableNumber={TableNumber}, PressureRating={PressureRating}, ImportedCount={ImportedCount}, WarningCount={WarningCount}",
                normalizedBatchId,
                operatorName,
                request.DuplicateStrategy,
                committedSeries.SeriesName,
                committedSeries.StandardNumber,
                committedSeries.TableNumber,
                committedSeries.PressureRating,
                imported,
                batch.Rows.Sum(row => row.Warnings.Count));

            return new StandardImportCommitResponse
            {
                Success = true,
                Message = "规范数据导入成功。",
                BatchId = normalizedBatchId,
                ImportedCount = imported,
                WarningCount = batch.Rows.Sum(row => row.Warnings.Count)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "规范数据导入失败。BatchId={BatchId}, Operator={OperatorName}, Strategy={Strategy}, DatabaseType={DatabaseType}, Schema={Schema}", normalizedBatchId, operatorName, request.DuplicateStrategy, databaseType, GetSchemaName());
            throw;
        }
    }

    /// <summary>
    /// 只允许用户修改页面公开的四段名称，分类、系列编码和法兰类型等系统字段继续使用预览批次中的可信值。
    /// </summary>
    private static StandardSeriesDto BuildCommittedSeries(StandardSeriesDto previewSeries, StandardSeriesDto submittedSeries)
    {
        ArgumentNullException.ThrowIfNull(previewSeries);
        ArgumentNullException.ThrowIfNull(submittedSeries);

        string seriesName = RequireMetadataValue(submittedSeries.SeriesName, "规范注释名称");
        string standardNumber = RequireMetadataValue(submittedSeries.StandardNumber, "规范代号");
        string tableNumber = RequireMetadataValue(submittedSeries.TableNumber, "表号");
        string pressureRating = RequireMetadataValue(submittedSeries.PressureRating, "型号");

        return new StandardSeriesDto
        {
            CategoryId = previewSeries.CategoryId,
            FamilyCode = previewSeries.FamilyCode,
            FamilyName = previewSeries.FamilyName,
            SeriesCode = previewSeries.SeriesCode,
            SeriesName = seriesName,
            StandardNumber = standardNumber,
            TableNumber = tableNumber,
            PressureRating = pressureRating,
            FlangeType = previewSeries.FlangeType,
            FaceType = previewSeries.FaceType
        };
    }

    /// <summary>
    /// 校验用户编辑后的规范名称字段并统一去除首尾空白。
    /// </summary>
    private static string RequireMetadataValue(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{fieldName}不能为空。", nameof(value));

        return value.Trim();
    }

    /// <summary>
    /// 将统一身份查询结果转换为导入预览使用的三态结果。
    /// </summary>
    private static StandardImportIdentityResultDto ToIdentityResult(StandardIdentityResolveResponse identity)
    {
        string status = identity.MatchCount switch
        {
            0 => "NEW",
            1 => "EXISTING",
            _ => "CONFLICT"
        };

        StandardManagementSeriesDto? existingSeries = identity.IsUniqueMatch ? identity.Series : null;
        return new StandardImportIdentityResultDto
        {
            Status = status,
            MatchCount = identity.MatchCount,
            IsUniqueMatch = identity.IsUniqueMatch,
            Message = status == "NEW"
                ? "规范身份未命中，可以作为新规范导入。"
                : status == "EXISTING"
                    ? "规范身份已命中已有规范系列，请选择后续导入策略。"
                    : "规范身份匹配到多个系列，不能继续确认导入。",
            ExistingSeries = existingSeries,
            CurrentVersion = identity.IsUniqueMatch ? identity.CurrentVersion : null
        };
    }

    /// <summary>
    /// 按当前数据表的实际唯一标识精确查询已存在的规范系列，预览阶段只读取，不修改任何数据。
    /// </summary>
    private async Task<StandardImportDuplicateSeriesDto?> FindDuplicateSeriesAsync(
        StandardSeriesDto series,
        CancellationToken cancellationToken)
    {
        string databaseType = GetDatabaseType();
        string familyCode = NormalizeCode(series.FamilyCode);
        string seriesCode = NormalizeCode(series.SeriesCode);
        string standardNumber = NormalizeCode(series.StandardNumber);
        string tableNumber = NormalizeCode(series.TableNumber);
        string pressureRating = NormalizeCode(series.PressureRating);

        if (databaseType == "MYSQL")
        {
            const string sql = @"
SELECT
    ss.id AS SeriesId,
    ss.series_name AS SeriesName,
    ss.standard_number AS StandardNumber,
    ss.table_number AS TableNumber,
    ss.pressure_rating AS PressureRating,
    COUNT(sfr.id) AS RecordCount
FROM standard_series ss
INNER JOIN standard_families sf ON sf.id = ss.family_id AND sf.is_active = 1
LEFT JOIN standard_flange_records sfr ON sfr.series_id = ss.id AND sfr.is_active = 1
WHERE ss.is_active = 1
  AND sf.code = @FamilyCode
  AND ss.series_code = @SeriesCode
  AND ss.standard_number = @StandardNumber
  AND ss.table_number = @TableNumber
  AND ss.pressure_rating = @PressureRating
GROUP BY ss.id, ss.series_name, ss.standard_number, ss.table_number, ss.pressure_rating";

            await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await connection.QuerySingleOrDefaultAsync<StandardImportDuplicateSeriesDto>(
                new CommandDefinition(sql, new
                {
                    FamilyCode = familyCode,
                    SeriesCode = seriesCode,
                    StandardNumber = standardNumber,
                    TableNumber = tableNumber,
                    PressureRating = pressureRating
                }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        string schema = GetSchemaName();
        string dmSql = $@"
SELECT
    ss.ID AS SeriesId,
    ss.SERIES_NAME AS SeriesName,
    ss.STANDARD_NUMBER AS StandardNumber,
    ss.TABLE_NUMBER AS TableNumber,
    ss.PRESSURE_RATING AS PressureRating,
    COUNT(sfr.ID) AS RecordCount
FROM {schema}.STANDARD_SERIES ss
INNER JOIN {schema}.STANDARD_FAMILIES sf ON sf.ID = ss.FAMILY_ID AND sf.IS_ACTIVE = 1
LEFT JOIN {schema}.STANDARD_FLANGE_RECORDS sfr ON sfr.SERIES_ID = ss.ID AND sfr.IS_ACTIVE = 1
WHERE ss.IS_ACTIVE = 1
  AND sf.CODE = :FamilyCode
  AND ss.SERIES_CODE = :SeriesCode
  AND ss.STANDARD_NUMBER = :StandardNumber
  AND ss.TABLE_NUMBER = :TableNumber
  AND ss.PRESSURE_RATING = :PressureRating
GROUP BY ss.ID, ss.SERIES_NAME, ss.STANDARD_NUMBER, ss.TABLE_NUMBER, ss.PRESSURE_RATING";

        await using var dmConnection = new DmConnection(GetConnectionString("DM"));
        await dmConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await dmConnection.QuerySingleOrDefaultAsync<StandardImportDuplicateSeriesDto>(
            new CommandDefinition(dmSql, new
            {
                FamilyCode = familyCode,
                SeriesCode = seriesCode,
                StandardNumber = standardNumber,
                TableNumber = tableNumber,
                PressureRating = pressureRating
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static List<StandardImportRowDto> ParseWorkbook(Stream stream, StandardSeriesDto series)
    {
        using IWorkbook workbook = new XSSFWorkbook(stream);
        ISheet sheet = workbook.GetSheetAt(0);
        if (sheet == null || sheet.LastRowNum < 1)
        {
            throw new InvalidDataException("Excel 第一张工作表没有有效数据。");
        }

        IRow headerRow = sheet.GetRow(sheet.FirstRowNum)
            ?? throw new InvalidDataException("Excel 缺少表头行。");
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = headerRow.FirstCellNum; i < headerRow.LastCellNum; i++)
        {
            string header = NormalizeHeader(GetCellText(headerRow.GetCell(i)));
            if (!string.IsNullOrWhiteSpace(header))
            {
                headers[header] = i;
            }
        }

        string[] requiredHeaders =
        {
            "DN", "PipeOuterDiameterSeriesI", "PipeOuterDiameterSeriesII", "FlangeOuterDiameter",
            "BoltCircleDiameter", "BoltHoleDiameter", "BoltCount", "BoltSpecification",
            "FlangeThickness", "RaisedFaceHeight", "FlangeInnerDiameterSeriesI", "FlangeInnerDiameterSeriesII"
        };

        var missing = requiredHeaders
            .Where(required => !headers.ContainsKey(NormalizeHeader(required)))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"Excel 缺少必需列：{string.Join(", ", missing)}。");
        }

        var result = new List<StandardImportRowDto>();
        for (int rowIndex = sheet.FirstRowNum + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            IRow? row = sheet.GetRow(rowIndex);
            if (row == null || row.Cells.All(cell => string.IsNullOrWhiteSpace(GetCellText(cell))))
            {
                continue;
            }

            int rowNumber = rowIndex + 1;
            var errors = new List<string>();
            var warnings = new List<string>();
            string dn = NormalizeDn(GetValue(row, headers, "DN"));
            int dnValue = ParseDnValue(dn, errors, rowNumber);
            string pn = string.IsNullOrWhiteSpace(series.PressureRating) ? GetValue(row, headers, "PN") : series.PressureRating;

            decimal? pipeOdI = ReadDecimal(row, headers, "PipeOuterDiameterSeriesI", errors, rowNumber);
            decimal? pipeOdII = ReadDecimal(row, headers, "PipeOuterDiameterSeriesII", errors, rowNumber);
            decimal? flangeOd = ReadDecimal(row, headers, "FlangeOuterDiameter", errors, rowNumber);
            decimal? boltPcd = ReadDecimal(row, headers, "BoltCircleDiameter", errors, rowNumber);
            decimal? boltHole = ReadDecimal(row, headers, "BoltHoleDiameter", errors, rowNumber);
            int? boltCount = ReadInt(row, headers, "BoltCount", errors, rowNumber);
            decimal? flangeThickness = ReadDecimal(row, headers, "FlangeThickness", errors, rowNumber);
            decimal? raisedFaceHeight = ReadDecimal(row, headers, "RaisedFaceHeight", errors, rowNumber);
            decimal? flangeIdI = ReadDecimal(row, headers, "FlangeInnerDiameterSeriesI", errors, rowNumber);
            decimal? flangeIdII = ReadDecimal(row, headers, "FlangeInnerDiameterSeriesII", errors, rowNumber);
            string boltSpec = GetValue(row, headers, "BoltSpecification");
            string boltRawSuffix = GetOptionalValue(row, headers, "BoltRawSuffix");

            if (flangeOd.HasValue && boltPcd.HasValue && flangeOd.Value <= boltPcd.Value)
            {
                warnings.Add($"第 {rowNumber} 行：法兰外径 D 不大于螺栓孔中心圆 K，请核对标准原文。");
            }

            var record = new FlangeStandardRecordDto
            {
                SourceRowNumber = rowNumber,
                DN = dn,
                DNValue = dnValue,
                PN = NormalizeCode(pn),
                PipeOuterDiameterSeriesI = pipeOdI,
                PipeOuterDiameterSeriesII = pipeOdII,
                FlangeOuterDiameter = flangeOd,
                BoltCircleDiameter = boltPcd,
                BoltHoleDiameter = boltHole,
                BoltCount = boltCount,
                BoltSpecification = boltSpec,
                BoltRawSuffix = boltRawSuffix,
                FlangeThickness = flangeThickness,
                RaisedFaceHeight = raisedFaceHeight,
                FlangeInnerDiameterSeriesI = flangeIdI,
                FlangeInnerDiameterSeriesII = flangeIdII,
                Warnings = warnings
            };

            result.Add(new StandardImportRowDto
            {
                RowNumber = rowNumber,
                Record = record,
                Errors = errors,
                Warnings = warnings
            });
        }

        var duplicateKeys = result
            .Where(row => row.Record != null && row.Errors.Count == 0)
            .GroupBy(row => $"{row.Record!.DNValue}|{row.Record.PN}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var duplicate in duplicateKeys)
        {
            foreach (StandardImportRowDto row in duplicate)
            {
                row.Errors.Add($"DN/PN 重复：{duplicate.Key}。同一规范系列只能保留一条记录。");
            }
        }

        return result;
    }

    private static void ValidateJsonSeries(StandardSeriesDto series)
    {
        if (string.IsNullOrWhiteSpace(series.FamilyCode) ||
            string.IsNullOrWhiteSpace(series.FamilyName) ||
            string.IsNullOrWhiteSpace(series.SeriesCode) ||
            string.IsNullOrWhiteSpace(series.SeriesName) ||
            string.IsNullOrWhiteSpace(series.StandardNumber) ||
            string.IsNullOrWhiteSpace(series.TableNumber) ||
            string.IsNullOrWhiteSpace(series.PressureRating))
        {
            throw new InvalidDataException("JSON 缺少规范系列元数据，请按照模板填写完整。");
        }
    }

    private static List<StandardImportRowDto> ValidateJsonRows(
        IReadOnlyList<FlangeStandardRecordDto> records,
        string pressureRating)
    {
        var result = new List<StandardImportRowDto>();
        for (int index = 0; index < records.Count; index++)
        {
            FlangeStandardRecordDto record = records[index];
            int rowNumber = record.SourceRowNumber > 0 ? record.SourceRowNumber : index + 1;
            var errors = new List<string>();
            var warnings = new List<string>(record.Warnings ?? new List<string>());

            string dn = record.DN?.Trim().ToUpperInvariant() ?? string.Empty;
            int dnValue = 0;
            if (!dn.StartsWith("DN", StringComparison.Ordinal) ||
                !int.TryParse(dn[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out dnValue) ||
                dnValue <= 0)
            {
                errors.Add($"第 {rowNumber} 条：DN 必须是 DN50 等有效格式。");
            }
            else if (record.DNValue != 0 && record.DNValue != dnValue)
            {
                errors.Add($"第 {rowNumber} 条：DNValue 与 DN 不一致。");
            }

            string pn = string.IsNullOrWhiteSpace(record.PN) ? pressureRating : record.PN.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(pn)) errors.Add($"第 {rowNumber} 条：PN 不能为空。");
            if (!record.PipeOuterDiameterSeriesI.HasValue) errors.Add($"第 {rowNumber} 条：缺少 PipeOuterDiameterSeriesI。");
            if (!record.PipeOuterDiameterSeriesII.HasValue) errors.Add($"第 {rowNumber} 条：缺少 PipeOuterDiameterSeriesII。");
            if (!record.FlangeOuterDiameter.HasValue) errors.Add($"第 {rowNumber} 条：缺少 FlangeOuterDiameter。");
            if (!record.BoltCircleDiameter.HasValue) errors.Add($"第 {rowNumber} 条：缺少 BoltCircleDiameter。");
            if (!record.BoltHoleDiameter.HasValue) errors.Add($"第 {rowNumber} 条：缺少 BoltHoleDiameter。");
            if (!record.BoltCount.HasValue || record.BoltCount <= 0) errors.Add($"第 {rowNumber} 条：BoltCount 必须大于 0。");
            if (string.IsNullOrWhiteSpace(record.BoltSpecification)) errors.Add($"第 {rowNumber} 条：BoltSpecification 不能为空。");
            if (!record.FlangeThickness.HasValue) errors.Add($"第 {rowNumber} 条：缺少 FlangeThickness。");
            if (!record.RaisedFaceHeight.HasValue) errors.Add($"第 {rowNumber} 条：缺少 RaisedFaceHeight。");
            if (!record.FlangeInnerDiameterSeriesI.HasValue) errors.Add($"第 {rowNumber} 条：缺少 FlangeInnerDiameterSeriesI。");
            if (!record.FlangeInnerDiameterSeriesII.HasValue) errors.Add($"第 {rowNumber} 条：缺少 FlangeInnerDiameterSeriesII。");

            if (record.FlangeOuterDiameter.HasValue && record.BoltCircleDiameter.HasValue &&
                record.FlangeOuterDiameter.Value <= record.BoltCircleDiameter.Value)
            {
                warnings.Add($"第 {rowNumber} 条：法兰外径 D 不大于螺栓孔中心圆 K，请核对标准原文。");
            }

            result.Add(new StandardImportRowDto
            {
                RowNumber = rowNumber,
                Record = new FlangeStandardRecordDto
                {
                    SourceRowNumber = rowNumber,
                    DN = dn,
                    DNValue = dnValue,
                    PN = pn,
                    PipeOuterDiameterSeriesI = record.PipeOuterDiameterSeriesI,
                    PipeOuterDiameterSeriesII = record.PipeOuterDiameterSeriesII,
                    FlangeOuterDiameter = record.FlangeOuterDiameter,
                    BoltCircleDiameter = record.BoltCircleDiameter,
                    BoltHoleDiameter = record.BoltHoleDiameter,
                    BoltCount = record.BoltCount,
                    BoltSpecification = record.BoltSpecification?.Trim() ?? string.Empty,
                    BoltRawSuffix = record.BoltRawSuffix,
                    FlangeThickness = record.FlangeThickness,
                    RaisedFaceHeight = record.RaisedFaceHeight,
                    FlangeInnerDiameterSeriesI = record.FlangeInnerDiameterSeriesI,
                    FlangeInnerDiameterSeriesII = record.FlangeInnerDiameterSeriesII,
                    RawValues = record.RawValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    Warnings = warnings
                },
                Errors = errors,
                Warnings = warnings
            });
        }

        var duplicateKeys = result
            .Where(row => row.Record != null && row.Errors.Count == 0)
            .GroupBy(row => $"{row.Record!.DNValue}|{row.Record.PN}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);
        foreach (var duplicate in duplicateKeys)
        {
            foreach (StandardImportRowDto row in duplicate)
            {
                row.Errors.Add($"DN/PN 重复：{duplicate.Key}。同一规范系列只能保留一条记录。");
            }
        }

        return result;
    }

    private async Task<int> CommitMySqlAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(GetConnectionString("MYSQL"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long familyId = await EnsureMySqlFamilyAsync(connection, transaction, batch.Series, cancellationToken).ConfigureAwait(false);
            long seriesId = await EnsureMySqlSeriesAsync(connection, transaction, familyId, batch.Series, cancellationToken).ConfigureAwait(false);
            foreach (StandardImportRowDto row in batch.Rows)
            {
                await UpsertMySqlRecordAsync(connection, transaction, seriesId, row.Record!, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return batch.Rows.Count;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<int> CommitDmAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        string schema = GetSchemaName();
        await using var connection = new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // DmProvider 可能未实现 DbConnection.BeginTransactionAsync 的完整异步路径，
        // 使用同步事务创建后仍通过 Dapper 的异步命令执行写入。
        using System.Data.Common.DbTransaction transaction = connection.BeginTransaction();
        try
        {
            long familyId = await EnsureDmFamilyAsync(connection, transaction, schema, batch.Series, cancellationToken).ConfigureAwait(false);
            long seriesId = await EnsureDmSeriesAsync(connection, transaction, schema, familyId, batch.Series, cancellationToken).ConfigureAwait(false);
            foreach (StandardImportRowDto row in batch.Rows)
            {
                await UpsertDmRecordAsync(connection, transaction, schema, seriesId, row.Record!, cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
            return batch.Rows.Count;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<long> EnsureMySqlFamilyAsync(MySqlConnection connection, MySqlTransaction transaction, StandardSeriesDto series, CancellationToken cancellationToken)
    {
        long? existing = await connection.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT id FROM standard_families WHERE code=@Code", new { Code = series.FamilyCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (existing.HasValue) return existing.Value;
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO standard_families(code,name,is_active,created_at,updated_at) VALUES(@Code,@Name,1,NOW(),NOW())", new { Code = series.FamilyCode, Name = series.FamilyName }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT id FROM standard_families WHERE code=@Code", new { Code = series.FamilyCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task<long> EnsureMySqlSeriesAsync(MySqlConnection connection, MySqlTransaction transaction, long familyId, StandardSeriesDto series, CancellationToken cancellationToken)
    {
        const string select = "SELECT id FROM standard_series WHERE family_id=@FamilyId AND series_code=@SeriesCode AND standard_number=@StandardNumber AND table_number=@TableNumber AND pressure_rating=@PressureRating";
        object args = new { FamilyId = familyId, SeriesCode = series.SeriesCode, StandardNumber = series.StandardNumber, TableNumber = series.TableNumber, PressureRating = series.PressureRating };
        long? existing = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(select, args, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (existing.HasValue)
        {
            if (series.CategoryId.HasValue)
                await connection.ExecuteAsync(new CommandDefinition("UPDATE standard_series SET category_id=@CategoryId,updated_at=NOW() WHERE id=@Id AND is_active=1", new { Id = existing.Value, CategoryId = series.CategoryId.Value }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return existing.Value;
        }
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO standard_series(family_id,category_id,series_code,series_name,standard_number,table_number,pressure_rating,flange_type,face_type,is_active,created_at,updated_at) VALUES(@FamilyId,@CategoryId,@SeriesCode,@SeriesName,@StandardNumber,@TableNumber,@PressureRating,@FlangeType,@FaceType,1,NOW(),NOW())", new { FamilyId = familyId, CategoryId = series.CategoryId, series.SeriesCode, series.SeriesName, series.StandardNumber, series.TableNumber, series.PressureRating, series.FlangeType, series.FaceType }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(select, args, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task UpsertMySqlRecordAsync(MySqlConnection connection, MySqlTransaction transaction, long seriesId, FlangeStandardRecordDto record, CancellationToken cancellationToken)
    {
        const string delete = "DELETE FROM standard_flange_records WHERE series_id=@SeriesId AND dn_value=@DNValue AND pn=@PN";
        await connection.ExecuteAsync(new CommandDefinition(delete, new { SeriesId = seriesId, record.DNValue, record.PN }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        const string insert = @"INSERT INTO standard_flange_records(series_id,source_row_number,dn,dn_value,pn,pipe_outer_diameter_i,pipe_outer_diameter_ii,flange_outer_diameter,bolt_circle_diameter,bolt_hole_diameter,bolt_count,bolt_specification,bolt_raw_suffix,flange_thickness,raised_face_height,flange_inner_diameter_i,flange_inner_diameter_ii,raw_values_json,warnings_json,is_active,created_at,updated_at) VALUES(@SeriesId,@SourceRowNumber,@DN,@DNValue,@PN,@PipeOuterDiameterSeriesI,@PipeOuterDiameterSeriesII,@FlangeOuterDiameter,@BoltCircleDiameter,@BoltHoleDiameter,@BoltCount,@BoltSpecification,@BoltRawSuffix,@FlangeThickness,@RaisedFaceHeight,@FlangeInnerDiameterSeriesI,@FlangeInnerDiameterSeriesII,@RawValuesJson,@WarningsJson,1,NOW(),NOW())";
        await connection.ExecuteAsync(new CommandDefinition(insert, ToParameters(seriesId, record), transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task<long> EnsureDmFamilyAsync(DmConnection connection, System.Data.IDbTransaction transaction, string schema, StandardSeriesDto series, CancellationToken cancellationToken)
    {
        long? existing = await connection.ExecuteScalarAsync<long?>(new CommandDefinition($"SELECT ID FROM {schema}.STANDARD_FAMILIES WHERE CODE=:Code", new { Code = series.FamilyCode }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (existing.HasValue) return existing.Value;
        long id = await NextDmIdAsync(connection, transaction, $"{schema}.STANDARD_FAMILIES", cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {schema}.STANDARD_FAMILIES(ID,CODE,NAME,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES(:Id,:Code,:Name,1,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)", new { Id = id, Code = series.FamilyCode, Name = series.FamilyName }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return id;
    }

    private async Task<long> EnsureDmSeriesAsync(DmConnection connection, System.Data.IDbTransaction transaction, string schema, long familyId, StandardSeriesDto series, CancellationToken cancellationToken)
    {
        const string condition = "FAMILY_ID=:FamilyId AND SERIES_CODE=:SeriesCode AND STANDARD_NUMBER=:StandardNumber AND TABLE_NUMBER=:TableNumber AND PRESSURE_RATING=:PressureRating";
        long? existing = await connection.ExecuteScalarAsync<long?>(new CommandDefinition($"SELECT ID FROM {schema}.STANDARD_SERIES WHERE {condition}", new { FamilyId = familyId, SeriesCode = series.SeriesCode, StandardNumber = series.StandardNumber, TableNumber = series.TableNumber, PressureRating = series.PressureRating }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (existing.HasValue)
        {
            if (series.CategoryId.HasValue)
                await connection.ExecuteAsync(new CommandDefinition($"UPDATE {schema}.STANDARD_SERIES SET CATEGORY_ID=:CategoryId,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID=:Id AND IS_ACTIVE=1", new { Id = existing.Value, CategoryId = series.CategoryId.Value }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return existing.Value;
        }
        long id = await NextDmIdAsync(connection, transaction, $"{schema}.STANDARD_SERIES", cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {schema}.STANDARD_SERIES(ID,FAMILY_ID,CATEGORY_ID,SERIES_CODE,SERIES_NAME,STANDARD_NUMBER,TABLE_NUMBER,PRESSURE_RATING,FLANGE_TYPE,FACE_TYPE,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES(:Id,:FamilyId,:CategoryId,:SeriesCode,:SeriesName,:StandardNumber,:TableNumber,:PressureRating,:FlangeType,:FaceType,1,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)", new { Id = id, FamilyId = familyId, CategoryId = series.CategoryId, series.SeriesCode, series.SeriesName, series.StandardNumber, series.TableNumber, series.PressureRating, series.FlangeType, series.FaceType }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return id;
    }

    private async Task UpsertDmRecordAsync(DmConnection connection, System.Data.IDbTransaction transaction, string schema, long seriesId, FlangeStandardRecordDto record, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {schema}.STANDARD_FLANGE_RECORDS WHERE SERIES_ID=:SeriesId AND DN_VALUE=:DNValue AND PN=:PN", new { SeriesId = seriesId, record.DNValue, record.PN }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        const string insert = @"INSERT INTO {SCHEMA}.STANDARD_FLANGE_RECORDS(ID,SERIES_ID,SOURCE_ROW_NUMBER,DN,DN_VALUE,PN,PIPE_OUTER_DIAMETER_I,PIPE_OUTER_DIAMETER_II,FLANGE_OUTER_DIAMETER,BOLT_CIRCLE_DIAMETER,BOLT_HOLE_DIAMETER,BOLT_COUNT,BOLT_SPECIFICATION,BOLT_RAW_SUFFIX,FLANGE_THICKNESS,RAISED_FACE_HEIGHT,FLANGE_INNER_DIAMETER_I,FLANGE_INNER_DIAMETER_II,RAW_VALUES_JSON,WARNINGS_JSON,IS_ACTIVE,CREATED_AT,UPDATED_AT) VALUES(:Id,:SeriesId,:SourceRowNumber,:DN,:DNValue,:PN,:PipeOuterDiameterSeriesI,:PipeOuterDiameterSeriesII,:FlangeOuterDiameter,:BoltCircleDiameter,:BoltHoleDiameter,:BoltCount,:BoltSpecification,:BoltRawSuffix,:FlangeThickness,:RaisedFaceHeight,:FlangeInnerDiameterSeriesI,:FlangeInnerDiameterSeriesII,:RawValuesJson,:WarningsJson,1,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)";
        long id = await NextDmIdAsync(connection, transaction, $"{schema}.STANDARD_FLANGE_RECORDS", cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(insert.Replace("{SCHEMA}", schema, StringComparison.Ordinal), ToParameters(id, seriesId, record), transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task<long> NextDmIdAsync(DmConnection connection, System.Data.IDbTransaction transaction, string table, CancellationToken cancellationToken) => await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COALESCE(MAX(ID),0)+1 FROM {table}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static object ToParameters(long seriesId, FlangeStandardRecordDto record) => ToParameters(0, seriesId, record);

    private static object ToParameters(long id, long seriesId, FlangeStandardRecordDto record) => new
    {
        Id = id,
        SeriesId = seriesId,
        record.SourceRowNumber,
        record.DN,
        record.DNValue,
        record.PN,
        record.PipeOuterDiameterSeriesI,
        record.PipeOuterDiameterSeriesII,
        record.FlangeOuterDiameter,
        record.BoltCircleDiameter,
        record.BoltHoleDiameter,
        record.BoltCount,
        record.BoltSpecification,
        record.BoltRawSuffix,
        record.FlangeThickness,
        record.RaisedFaceHeight,
        record.FlangeInnerDiameterSeriesI,
        record.FlangeInnerDiameterSeriesII,
        RawValuesJson = JsonSerializer.Serialize(record.RawValues),
        WarningsJson = JsonSerializer.Serialize(record.Warnings)
    };

    private static string GetValue(IRow row, IReadOnlyDictionary<string, int> headers, string name) => GetOptionalValue(row, headers, name);

    private static string GetOptionalValue(IRow row, IReadOnlyDictionary<string, int> headers, string name) => headers.TryGetValue(NormalizeHeader(name), out int index) ? GetCellText(row.GetCell(index)).Trim() : string.Empty;

    private static decimal? ReadDecimal(IRow row, IReadOnlyDictionary<string, int> headers, string name, List<string> errors, int rowNumber)
    {
        string value = GetValue(row, headers, name);
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result)) return result;
        errors.Add($"第 {rowNumber} 行：{name} 不是有效数值。实际值：{value}");
        return null;
    }

    private static int? ReadInt(IRow row, IReadOnlyDictionary<string, int> headers, string name, List<string> errors, int rowNumber)
    {
        string value = GetValue(row, headers, name);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) return result;
        errors.Add($"第 {rowNumber} 行：{name} 不是有效整数。实际值：{value}");
        return null;
    }

    private static int ParseDnValue(string dn, List<string> errors, int rowNumber)
    {
        if (dn.StartsWith("DN", StringComparison.OrdinalIgnoreCase) && int.TryParse(dn[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0) return value;
        errors.Add($"第 {rowNumber} 行：DN 必须是 DN50 等格式。实际值：{dn}");
        return 0;
    }

    private static string NormalizeDn(string value)
    {
        string normalized = NormalizeCode(value).Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized.StartsWith("DN", StringComparison.Ordinal) ? normalized : $"DN{normalized}";
    }

    private static string NormalizeHeader(string value) => NormalizeCode(value).Replace("_", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string GetCellText(ICell? cell) => cell == null ? string.Empty : cell.CellType == CellType.Numeric ? cell.NumericCellValue.ToString(CultureInfo.InvariantCulture) : cell.ToString() ?? string.Empty;

    private string GetDatabaseType() => NormalizeCode(_configuration["Database:Type"]) == "MYSQL" ? "MYSQL" : "DM";
    private string GetSchemaName() => (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
    private string GetConnectionString(string type) => (_configuration["Database:ConnectionString"] ?? string.Empty).Trim() is { Length: > 0 } configured ? configured : (_configuration.GetConnectionString(type == "MYSQL" ? "MySQL" : "DM") ?? throw new InvalidOperationException($"缺少 {type} 数据库连接字符串配置。"));

    private sealed record ImportBatch(string BatchId, StandardSeriesDto Series, IReadOnlyList<StandardImportRowDto> Rows, DateTime CreatedAt);
}

/// <summary>导入确认结果。</summary>
public sealed class StandardImportCommitResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string BatchId { get; init; } = string.Empty;
    public int ImportedCount { get; init; }
    public int WarningCount { get; init; }
}
