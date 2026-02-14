# 开发计划 - 动态字段映射支持

## 1. 需求分析
当前插件的 `WorkOrder` 实体类使用硬编码的 JSON 属性名（如 "工单号"）。为了提高插件的通用性，需要允许用户自定义输入数据中的字段名称映射。

## 2. 拟用方案
- 引入 `FieldMappingItem` 类，用于存储“内部字段”与“外部字段”的对应关系。
- 在 `APSPluginServerCommand` 中添加 `List<FieldMappingItem>` 属性。
- 动态解析输入数据，不再依赖 `Newtonsoft.Json` 的自动反序列化到固定类，而是手动根据映射提取值。

## 3. 详细设计

### 3.1 核心字段定义 (Internal Fields)
- `OrderID`: 工单号
- `ProcessingTime`: 加工时长
- `DueDate`: 交货日期
- `Priority`: 订单优先级
- `ArrivalTime`: 到达时间

### 3.2 映射项类
```csharp
public class FieldMappingItem : ObjectPropertyBase
{
    [DisplayName("内部字段")]
    public InternalField InternalField { get; set; }

    [FormulaProperty]
    [DisplayName("外部字段名称")]
    public object ExternalFieldName { get; set; }
}
```

### 3.3 命令属性定义
```csharp
[ListProperty]
[DisplayName("字段名称映射")]
public List<FieldMappingItem> FieldMappings { get; set; }
```

### 3.3 校验逻辑
- 如果选用了 `SPT` 规则，但未映射 `ProcessingTime` 或输入数据中缺少该字段，则报错。
- 如果选用了 `EDD` 规则，但未映射 `DueDate` 或输入数据中缺少该字段，则报错。
- `Priority` 缺失时默认为 1。
- `ArrivalTime` 缺失时默认为当前时间。

## 4. 执行步骤
1. 定义枚举 `InternalField`。
2. 定义 `FieldMappingItem` 类。
3. 修改 `APSPluginServerCommand` 增加 `FieldMappings` 属性。
4. 在 `ExecuteAsync` 中实现动态提取逻辑：
   - 将 `rawWorkOrders` 转换为 `IEnumerable<dynamic>` 或 `List<JObject>`。
   - 遍历每一项，根据映射提取值。
   - 执行规则检查。
   - 执行排序。
5. 更新 `ToString()` 方法。

## 5. 参考文档
- [列表属性定义](../references/Unified_Properties.md)
- [公式解析](../references/SDK_BestPractices.md)
