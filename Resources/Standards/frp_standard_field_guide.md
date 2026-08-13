# 玻璃钢管和管件规范整理说明

## 使用范围

本说明用于整理玻璃钢（FRP/GRP）管道和管件的规范数据，不代表任何具体国家标准或厂商样本的数值要求。实际数值必须以项目采用的标准、设计院规定或厂家技术文件为准。

## 一、玻璃钢管字段

### 识别字段

- `DN`：公称尺寸，例如 `DN100`。
- `PN`：公称压力，例如 `PN10`。
- `StandardNumber`：标准号、设计规定或厂家标准编号。
- `ProductSeries`：产品系列或管道系列。

### 尺寸字段

- `OuterDiameter`：外径，单位 mm。
- `InnerDiameter`：内径，单位 mm。
- `WallThickness`：结构壁厚，单位 mm。
- `StandardLength`：标准长度或定尺长度，单位 mm。

### 设计条件

- `StiffnessClass`：环刚度等级或刚度类别。
- `DesignPressure`：设计压力，单位 MPa。
- `DesignTemperature`：设计温度，单位 °C。
- `Medium`：输送介质。

### 材料和制造字段

- `ResinSystem`：树脂体系。
- `Reinforcement`：增强材料及缠绕/铺层说明。
- `CorrosionBarrier`：内衬或耐腐蚀层说明。
- `JointType`：连接方式。
- `ConnectionSpecification`：连接端具体规格。
- `UnitWeight`：理论重量，单位 kg/m 或 kg/件，必须在表头或说明中明确。

## 二、玻璃钢管件字段

### 类型和连接尺寸

- `FittingType`：管件类型，例如 `ELBOW`、`TEE`、`REDUCER`、`CROSS`、`CAP`。
- `DN1`：主管或入口公称尺寸。
- `DN2`：出口或支管公称尺寸。
- `DN3`：第三个连接端公称尺寸，非必要时留空。
- `JointType`：连接方式。
- `ConnectionSpecification`：连接端具体规格。

### 结构尺寸

- `Angle`：弯头角度，单位 °。
- `CenterlineRadius`：弯曲中心半径，单位 mm。
- `OuterDiameter1`：第一连接端外径，单位 mm。
- `OuterDiameter2`：第二连接端外径，单位 mm。
- `WallThickness`：管件壁厚，单位 mm。
- `FaceToFaceLength`：面对面尺寸，单位 mm。
- `EndToEndLength`：端到端尺寸，单位 mm。
- `BranchLength`：支管长度，单位 mm。

### 设计和材料字段

- `PN`：公称压力。
- `DesignPressure`：设计压力，单位 MPa。
- `DesignTemperature`：设计温度，单位 °C。
- `Medium`：输送介质。
- `ResinSystem`：树脂体系。
- `Reinforcement`：增强材料及制造说明。
- `CorrosionBarrier`：耐腐蚀层说明。
- `UnitWeight`：理论重量，单位 kg/件。

## 三、导入规则建议

1. `DN1`、`DN2`、`DN3` 统一使用 `DN100` 格式。
2. 所有尺寸字段统一使用 mm，压力统一使用 MPa，温度统一使用 °C。
3. 同一规范系列中，记录唯一键建议为：
   - 管子：`DN + PN + ProductSeries + StiffnessClass + Material`。
   - 管件：`FittingType + DN1 + DN2 + DN3 + Angle + PN + ProductSeries`。
4. 原始标准中没有的字段留空，不要用 `0` 代替未知值。
5. `StandardNumber`、`SourceTable`、`SourceRowNumber` 必须保留，便于追溯。
6. 玻璃钢数据中的“壁厚”可能包括结构层、耐腐蚀层和总厚度，导入前必须确认字段含义，必要时拆成多个字段。
7. 环刚度、压力等级、温度等级和树脂体系不能仅根据文件名推断，必须以原始表格或技术说明为准。

## 四、当前模板文件

- `frp_pipe_standard_data_template.csv`
- `frp_fitting_standard_data_template.csv`

CSV 文件可以使用 Excel 打开，补充数据后另存为 `.xlsx`。模板中的示例行仅用于说明格式，不代表设计值。
