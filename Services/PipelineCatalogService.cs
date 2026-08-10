using GB_NewCadPlus_IV.UploadApi.Models;

namespace GB_NewCadPlus_IV.UploadApi.Services;

/// <summary>
/// 管道通用字段目录服务。
/// 第一阶段使用代码内的稳定契约，后续可以迁移到服务器配置或规范库表。
/// </summary>
public sealed class PipelineCatalogService
{
    private readonly ILogger<PipelineCatalogService> _logger;

    /// <summary>
    /// 创建管道字段目录服务。
    /// </summary>
    public PipelineCatalogService(ILogger<PipelineCatalogService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取管道通用字段和进口/出口视觉样式。
    /// </summary>
    public PipelineFieldCatalogResponse GetFieldCatalog()
    {
        PipelineFieldDefinitionDto[] fields = BuildFields();
        PipelineRoleStyleDto[] styles = BuildRoleStyles();

        _logger.LogInformation(
            "管道字段目录读取完成：FieldCount={FieldCount}, RoleStyleCount={RoleStyleCount}",
            fields.Length,
            styles.Length);

        return new PipelineFieldCatalogResponse
        {
            Success = true,
            Message = "管道通用字段目录读取成功。",
            Fields = fields,
            RoleStyles = styles
        };
    }

    /// <summary>
    /// 获取字段默认值。
    /// </summary>
    public PipelineDefaultsResponse GetDefaults()
    {
        Dictionary<string, string> attributes = BuildFields()
            .Where(field => !string.IsNullOrWhiteSpace(field.Tag))
            .ToDictionary(field => field.Tag, field => field.DefaultValue ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("管道默认值读取完成：AttributeCount={AttributeCount}", attributes.Count);

        return new PipelineDefaultsResponse
        {
            Success = true,
            Message = "管道通用默认值读取成功。",
            Attributes = attributes
        };
    }

    /// <summary>
    /// 创建统一字段定义。
    /// </summary>
    private static PipelineFieldDefinitionDto[] BuildFields()
    {
        return new[]
        {
            Field("PIPELINETITLE", "管道提示标题", "350-AR-0000-1.0G1", PipelineFieldDataTypes.Text, false, false, "基本信息", 10),
            Field("TAG_NO", "管段号、位号", "0001", PipelineFieldDataTypes.Text, true, true, "基本信息", 20),
            Field("NAME", "名称", "浆液管道", PipelineFieldDataTypes.Text, false, true, "基本信息", 30),
            Field("MODEL", "规格型号", "DN150 Sch40 碳钢+衬胶", PipelineFieldDataTypes.Text, false, true, "基本信息", 40),
            Field("DRAWINGNO.STANDARDNO", "图号、设计标准", "GB/T 20801", PipelineFieldDataTypes.Select, false, true, "标准与检验", 50,
                "GB/T 20801", "GB/T 8163", "GB/T 9711", "HG/T 20553"),
            Field("STRUCT_LEN_STD", "结构长度标准", "按设计院标准", PipelineFieldDataTypes.Select, false, true, "标准与检验", 60,
                "按设计院标准", "按厂家标准"),
            Field("START_POINT", "起始点", string.Empty, PipelineFieldDataTypes.Text, false, false, "连接关系", 70),
            Field("END_POINT", "终点", string.Empty, PipelineFieldDataTypes.Text, false, false, "连接关系", 80),
            Field("FLG_STD", "法兰标准", "HG/T 20592", PipelineFieldDataTypes.Select, false, true, "标准与检验", 90,
                "HG/T 20592", "GB/T 9119", "ASME B16.5", "JB/T 79"),
            Field("TEST_STD", "试验与检验标准", "GB/T 20801", PipelineFieldDataTypes.Select, false, true, "标准与检验", 100,
                "GB/T 13927", "GB/T 20801", "无损检测比例"),
            Field("DN", "公称通径", "DN150", PipelineFieldDataTypes.Select, true, true, "设计条件", 110,
                "DN15", "DN20", "DN25", "DN32", "DN40", "DN50", "DN65", "DN80", "DN100", "DN125", "DN150", "DN200", "DN250", "DN300", "DN350", "DN400", "DN450", "DN500", "DN600", "DN700", "DN800", "DN900", "DN1000"),
            Field("PIPE_OD", "管道外径（mm）", "168.3", PipelineFieldDataTypes.Number, false, true, "尺寸计算", 120),
            Field("PIPE_ID", "管道内径（mm）", "按计算", PipelineFieldDataTypes.Number, false, true, "尺寸计算", 130),
            Field("PIPE_THK", "管道壁厚（mm）", "5.5", PipelineFieldDataTypes.Number, false, true, "尺寸计算", 140),
            Field("SCHEDULE", "壁厚等级", "Sch40", PipelineFieldDataTypes.Select, false, true, "尺寸计算", 150,
                "Sch10", "Sch20", "Sch30", "Sch40", "Sch60", "Sch80", "Sch100", "Sch120", "Sch140", "Sch160", "STD", "XS", "XXS"),
            Field("PN", "公称压力", "PN10", PipelineFieldDataTypes.Select, true, true, "设计条件", 160,
                "PN6", "PN10", "PN16", "PN25", "PN40", "PN63", "PN100"),
            Field("CLASS", "压力等级", "Class150", PipelineFieldDataTypes.Select, false, true, "设计条件", 170,
                "Class150", "Class300", "Class600", "Class900"),
            Field("WORK_PRESSURE", "工作压力（MPa）", "0.6", PipelineFieldDataTypes.Number, false, true, "设计条件", 180),
            Field("DESIGN_PRESSURE", "设计压力（MPa）", "1", PipelineFieldDataTypes.Number, false, true, "设计条件", 190),
            Field("DESIGN_TEMP", "设计温度（℃）", "80", PipelineFieldDataTypes.Number, false, true, "设计条件", 200),
            Field("WORK_TEMP", "工作温度（℃）", "60~80", PipelineFieldDataTypes.Text, false, true, "设计条件", 210),
            Field("TEMP_RANGE", "适用温度范围（℃）", "-20~120", PipelineFieldDataTypes.Text, false, true, "设计条件", 220),
            Field("MEDIUM", "适用介质", "石灰石浆液", PipelineFieldDataTypes.Select, true, true, "介质与分类", 230,
                "石灰石浆液", "石膏浆液", "原烟气", "净烟气", "工艺水", "压缩空气", "蒸汽", "酸碱介质"),
            Field("PIPE_TYPE", "管道类型", "浆液管道", PipelineFieldDataTypes.Select, false, true, "介质与分类", 240,
                "工艺管道", "浆液管道", "烟道", "冲洗水管道", "压缩空气管道", "蒸汽管道"),
            Field("PIPE_CLASS", "管道等级", "B2", PipelineFieldDataTypes.Select, false, true, "介质与分类", 250,
                "A1", "B2", "C3"),
            Field("CONN_TYPE", "连接方式", "法兰连接", PipelineFieldDataTypes.Select, false, true, "材料与连接", 260,
                "法兰连接", "对焊连接", "承插焊", "卡箍", "螺纹连接"),
            Field("PIPE_MATL", "管道材质", "碳钢+衬胶", PipelineFieldDataTypes.Select, false, true, "材料与连接", 270,
                "碳钢+衬胶", "碳钢", "不锈钢304", "不锈钢316L", "双相钢2205", "玻璃钢FRP", "PPH", "PVDF", "钛材"),
            Field("LINING_MATL", "衬里材质", "丁基橡胶", PipelineFieldDataTypes.Select, false, true, "材料与连接", 280,
                "无", "丁基橡胶IIR", "EPDM", "氯丁橡胶CR", "丁腈橡胶NBR", "天然橡胶NR", "PTFE", "F46", "PPH", "耐磨聚氨酯"),
            Field("LINING_THK", "衬里厚度（mm）", "5", PipelineFieldDataTypes.Number, false, true, "材料与连接", 290,
                "3", "4", "5", "6", "8"),
            Field("LINING_PROC", "衬里工艺", "整体硫化衬胶", PipelineFieldDataTypes.Select, false, true, "材料与连接", 300,
                "整体硫化衬胶", "模压衬胶", "喷涂衬里", "无衬里"),
            Field("PIPE_LENGTH", "管道长度（mm）", "按实际", PipelineFieldDataTypes.Number, false, false, "计算结果", 310),
            Field("FLOW_VEL", "设计流速（m/s）", "2", PipelineFieldDataTypes.Number, false, true, "计算条件", 320),
            Field("FLOW_RATE", "介质流量（m³/h）", "200", PipelineFieldDataTypes.Number, false, true, "计算条件", 330),
            Field("PIPE_WEIGHT", "管道计算重量（kg/m）", "按计算", PipelineFieldDataTypes.Number, false, false, "计算结果", 340),
            Field("PIPE_COATING", "外部防腐涂层", "环氧富锌", PipelineFieldDataTypes.Select, false, true, "防腐保温", 350,
                "环氧富锌", "聚氨酯", "氯化橡胶", "玻璃鳞片", "无机富锌", "无"),
            Field("COATING_THK", "涂层厚度（μm）", "200", PipelineFieldDataTypes.Number, false, true, "防腐保温", 360,
                "150", "200", "250", "300"),
            Field("INSUL_MATL", "保温材料", "岩棉", PipelineFieldDataTypes.Select, false, true, "防腐保温", 370,
                "岩棉", "玻璃棉", "硅酸铝棉", "聚氨酯", "橡塑海绵", "无"),
            Field("INSUL_THK", "保温层厚度（mm）", "80", PipelineFieldDataTypes.Number, false, true, "防腐保温", 380,
                "50", "80", "100", "120", "150", "200"),
            Field("INSUL_TYPE", "保温方式", "保温", PipelineFieldDataTypes.Select, false, true, "防腐保温", 390,
                "保温", "保冷", "伴热保温", "无"),
            Field("HEAT_TRACE", "伴热类型", "无", PipelineFieldDataTypes.Select, false, true, "防腐保温", 400,
                "电伴热", "蒸汽伴热", "热水伴热", "无"),
            Field("HEAT_TRACE_POWER", "电伴热功率（W/m）", "按需", PipelineFieldDataTypes.Number, false, true, "防腐保温", 410),
            Field("NDT_RATIO", "无损检测比例（%）", "20", PipelineFieldDataTypes.Number, false, true, "标准与检验", 420,
                "0", "10", "20", "30", "50", "100"),
            Field("NDT_TYPE", "无损检测方法", "射线RT", PipelineFieldDataTypes.Select, false, true, "标准与检验", 430,
                "射线RT", "超声UT", "磁粉MT", "渗透PT", "无"),
            Field("PRESSURE_LOSS", "允许压力损失（kPa/100m）", "按计算", PipelineFieldDataTypes.Number, false, true, "计算条件", 440),
            Field("ROUGHNESS", "管道内壁粗糙度（mm）", "0.01", PipelineFieldDataTypes.Number, false, true, "计算条件", 450),
            Field("MAX_BENDING", "允许弯曲半径（m）", "按计算", PipelineFieldDataTypes.Number, false, true, "计算条件", 460),
            Field("EXPANSION", "热膨胀量（mm/100m）", "按计算", PipelineFieldDataTypes.Number, false, true, "计算条件", 470),
            Field("CLEANING_REQ", "清洁度要求", "酸洗钝化", PipelineFieldDataTypes.Select, false, true, "标准与检验", 480,
                "酸洗钝化", "喷砂除锈", "脱脂", "无"),
            Field("HOT\\SOUND_ISOLACODE", "隔热隔声代号", "G1", PipelineFieldDataTypes.Text, false, true, "防腐保温", 490),
            Field("IS_ANTICORRO", "是否防腐", string.Empty, PipelineFieldDataTypes.Boolean, false, true, "防腐保温", 500,
                "是", "否"),
            Field("PUMP_BEFORE_AFTER", "泵前、后", "后", PipelineFieldDataTypes.Select, false, true, "基本信息", 510,
                "前", "后"),
            Field("QTY", "数量", "1", PipelineFieldDataTypes.Number, false, true, "基本信息", 520),
            Field("WEIGHT", "总重量（kg）", "按计算", PipelineFieldDataTypes.Number, false, false, "计算结果", 530),
            Field("SW_MODEL", "对应3D模型文件名", "管道_DN150_Sch40.SLDPRT", PipelineFieldDataTypes.Text, false, true, "基本信息", 540),
            Field("SYSTEM", "所属系统", "吸收系统", PipelineFieldDataTypes.Select, false, true, "基本信息", 550,
                "烟气系统", "吸收系统", "浆液制备系统", "石膏脱水系统", "废水系统"),
            Field("REMARK", "备注", "—", PipelineFieldDataTypes.MultiLine, false, true, "基本信息", 560)
        };
    }

    /// <summary>
    /// 创建进口/出口的差异化图面样式。
    /// 颜色索引是 CAD ACI 颜色，不影响业务属性。
    /// </summary>
    private static PipelineRoleStyleDto[] BuildRoleStyles()
    {
        return new[]
        {
            new PipelineRoleStyleDto
            {
                PipeRole = PipelineRoles.Import,
                DisplayName = "进口管道",
                TitleColorIndex = 3,
                FlowDirectionColorIndex = 3,
                FlowDirectionSymbol = "IMPORT_ARROW"
            },
            new PipelineRoleStyleDto
            {
                PipeRole = PipelineRoles.Export,
                DisplayName = "出口管道",
                TitleColorIndex = 2,
                FlowDirectionColorIndex = 2,
                FlowDirectionSymbol = "EXPORT_ARROW"
            }
        };
    }

    /// <summary>
    /// 统一创建字段，避免字段契约属性在多个位置重复赋值。
    /// </summary>
    private static PipelineFieldDefinitionDto Field(
        string tag,
        string prompt,
        string defaultValue,
        string dataType,
        bool required,
        bool editable,
        string group,
        int displayOrder,
        params string[] options)
    {
        return new PipelineFieldDefinitionDto
        {
            Tag = tag,
            Prompt = prompt,
            DefaultValue = defaultValue,
            DataType = dataType,
            Required = required,
            Editable = editable,
            Group = group,
            DisplayOrder = displayOrder,
            Options = options ?? Array.Empty<string>()
        };
    }
}
