using GB_NewCadPlus_IV.UploadApi.Models;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Globalization;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>使用数据库模板解析任意表头 Excel，仅生成预览数据，不写入数据库。</summary>
public sealed class DynamicStandardPreviewService
{
    private readonly StandardTemplateQueryService _templateQueryService;
    private readonly ILogger<DynamicStandardPreviewService> _logger;

    public DynamicStandardPreviewService(StandardTemplateQueryService templateQueryService, ILogger<DynamicStandardPreviewService> logger)
    {
        _templateQueryService = templateQueryService ?? throw new ArgumentNullException(nameof(templateQueryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DynamicStandardPreviewResponse> PreviewAsync(Stream source, string sourceFileName = "", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("动态预览步骤 1/5：开始读取 Excel。CanRead={CanRead}, Length={Length}", source?.CanRead ?? false, source?.Length ?? 0);
        ArgumentNullException.ThrowIfNull(source);
        using IWorkbook workbook = new XSSFWorkbook(source);
        ISheet sheet = workbook.GetSheetAt(0) ?? throw new InvalidDataException("Excel 第一张工作表不存在。");
        IRow headerRow = sheet.GetRow(sheet.FirstRowNum) ?? throw new InvalidDataException("Excel 缺少表头行。");
        List<HeaderCell> headers = ReadHeaders(headerRow);
        if (headers.Count == 0) throw new InvalidDataException("Excel 表头为空。");
        _logger.LogInformation("动态预览步骤 1/5：Excel 表头读取完成。Sheet={SheetName}, HeaderCount={HeaderCount}, FirstRow={FirstRow}", sheet.SheetName, headers.Count, sheet.FirstRowNum);

        StandardTemplateMatchResult match = await _templateQueryService.MatchAsync(headers.Select(header => header.Text).ToList(), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DynamicStandardPreviewColumnDto> columns = BuildColumns(headers, match);
        IReadOnlyList<string> unmappedHeaders = headers.Where(header => !match.HeaderMappings.ContainsKey(header.Text)).Select(header => header.Text).ToList();
        StandardTemplateDraftDto? templateDraft = match.Template == null
            ? BuildTemplateDraft(headers, sourceFileName)
            : null;
        var rows = new List<DynamicStandardPreviewRowDto>();
        _logger.LogInformation("动态预览步骤 4/5：开始解析数据行。LastRow={LastRow}, UnmappedHeaderCount={UnmappedHeaderCount}", sheet.LastRowNum, unmappedHeaders.Count);

        for (int rowIndex = sheet.FirstRowNum + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            IRow? excelRow = sheet.GetRow(rowIndex);
            if (excelRow == null || excelRow.Cells.All(cell => string.IsNullOrWhiteSpace(GetCellText(cell)))) continue;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            var warnings = new List<string>();
            foreach (HeaderCell header in headers)
            {
                string value = GetCellText(excelRow.GetCell(header.Index)).Trim();
                string key = match.HeaderMappings.TryGetValue(header.Text, out StandardTemplateColumnDto? column) ? column.FieldCode : header.Text;
                values[key] = value;
                if (column != null && !ValidateDataType(value, column.DataType)) errors.Add($"第 {rowIndex + 1} 行：{column.FieldName} 不是有效 {column.DataType} 值。");
            }
            if (match.Template != null)
            {
                foreach (StandardTemplateColumnDto column in match.Template.Columns.Where(column => column.IsRequired))
                {
                    if (!values.TryGetValue(column.FieldCode, out string? value) || string.IsNullOrWhiteSpace(value)) errors.Add($"第 {rowIndex + 1} 行：缺少必填字段 {column.FieldName}。");
                }
                if (unmappedHeaders.Count > 0) warnings.Add($"第 {rowIndex + 1} 行：包含 {unmappedHeaders.Count} 个未映射表头，原始值将保留供模板确认。");
            }
            rows.Add(new DynamicStandardPreviewRowDto { RowNumber = rowIndex + 1, Values = values, Errors = errors, Warnings = warnings });
        }

        int errorCount = rows.Sum(row => row.Errors.Count);
        int warningCount = rows.Sum(row => row.Warnings.Count);
        _logger.LogInformation("动态预览步骤 5/5：Excel 预览完成。RowCount={RowCount}, ErrorCount={ErrorCount}, WarningCount={WarningCount}", rows.Count, errorCount, warningCount);
        return new DynamicStandardPreviewResponse
        {
            Success = errorCount == 0,
            Message = match.Template == null ? "首次上传未匹配模板，已根据 Excel 表头生成模板草稿，请确认后保存模板。" : errorCount == 0 ? "模板匹配成功，请确认动态预览内容。" : "模板匹配成功，但存在数据校验错误。",
            IsTemplateMatched = match.Template != null,
            Template = match.Template,
            Columns = columns,
            Rows = rows,
            UnmappedHeaders = unmappedHeaders,
            ErrorCount = errorCount,
            WarningCount = warningCount
            ,TemplateDraft = templateDraft
        };
    }

    /// <summary>根据首次上传文件的表头生成模板草稿，默认 TEXT 且非必填。</summary>
    private static StandardTemplateDraftDto BuildTemplateDraft(IReadOnlyList<HeaderCell> headers, string sourceFileName)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourceFileName ?? string.Empty).Trim();
        string templateCode = NormalizeCode(fileName.Length == 0 ? "AUTO_XLSX_TEMPLATE" : fileName);
        if (templateCode.Length > 128) templateCode = templateCode[..128];
        return new StandardTemplateDraftDto
        {
            TemplateCode = templateCode,
            TemplateName = fileName.Length == 0 ? "自动生成 Excel 模板" : fileName,
            FileType = "XLSX",
            Columns = headers.Select((header, index) => new StandardTemplateDraftColumnDto
            {
                Header = header.Text,
                FieldCode = NormalizeCode(header.Text, index + 1),
                FieldName = header.Text,
                DataType = "TEXT",
                IsRequired = false,
                SortOrder = index
            }).ToList()
        };
    }

    private static string NormalizeCode(string value, int suffix = 0)
    {
        string code = new string((value ?? string.Empty).Trim().Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray()).Trim('_').ToUpperInvariant();
        if (code.Length == 0) code = "FIELD";
        return suffix > 0 ? $"{code}_{suffix}" : code;
    }

    private static List<HeaderCell> ReadHeaders(IRow row) => Enumerable.Range(row.FirstCellNum, row.LastCellNum - row.FirstCellNum).Select(index => new HeaderCell(index, GetCellText(row.GetCell(index)).Trim())).Where(header => header.Text.Length > 0).ToList();
    private static IReadOnlyList<DynamicStandardPreviewColumnDto> BuildColumns(IEnumerable<HeaderCell> headers, StandardTemplateMatchResult match) => headers.Select(header => match.HeaderMappings.TryGetValue(header.Text, out StandardTemplateColumnDto? column) ? new DynamicStandardPreviewColumnDto { Header = header.Text, FieldCode = column.FieldCode, FieldName = column.FieldName, DataType = column.DataType, Unit = column.Unit, IsRequired = column.IsRequired, IsMapped = true } : new DynamicStandardPreviewColumnDto { Header = header.Text, FieldCode = header.Text, FieldName = header.Text }).ToList();
    private static bool ValidateDataType(string value, string type)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (string.Equals(type, "DECIMAL", StringComparison.OrdinalIgnoreCase)) return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || decimal.TryParse(value, out _);
        if (string.Equals(type, "INTEGER", StringComparison.OrdinalIgnoreCase)) return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        return true;
    }
    private static string GetCellText(ICell? cell) => cell == null ? string.Empty : cell.CellType == CellType.Numeric ? cell.NumericCellValue.ToString(CultureInfo.InvariantCulture) : cell.ToString() ?? string.Empty;
    private sealed record HeaderCell(int Index, string Text);
}
