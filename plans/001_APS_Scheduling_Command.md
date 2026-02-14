# 开发计划：APS 工单排序服务端命令

## 1. 需求分析
实现一个服务端命令，接收一组工单数据和一种排序规则，返回排序后的工单列表。

### 输入参数：
- **工单集合 (WorkOrders)**：支持变量绑定的对象列表。每个对象包含：
    - 工单号 (OrderID)
    - 加工时长 (ProcessingTime)
    - 交货日期 (DueDate)
    - 订单优先级 (Priority)
    - 到达时间 (ArrivalTime)
- **规则选择 (Rule)**：枚举类型，包含 EDD, SPT, WSPT, CR。
- **返回结果变量名 (ResultVariableName)**：用于存储排序后列表的变量名。

### 核心逻辑：
根据选择的规则对工单列表进行排序：
- **EDD**: 按 `DueDate` 升序。
- **SPT**: 按 `ProcessingTime` 升序。
- **WSPT**: 按 `Priority / ProcessingTime` 降序（假设优先级越高数值越大，权重越大）。
- **CR**: 按 `(DueDate - Now) / ProcessingTime` 升序。

## 2. 拟用方案
- 修改 `APSPlugin/APSPluginServerCommand.cs` 实现业务逻辑。
- 参考模板：`assets/templates/ServerCommand.cs.txt`。

## 3. 精准引用
- [服务端命令基本结构](../references/ServerCommand/Basic_Structure.md)
- [添加枚举属性](../references/ServerCommand/Add_Property_Enum.md)
- [添加对象列表属性](../references/ServerCommand/Add_Property_ObjectList.md)
- [处理执行结果](../references/ServerCommand/Process_Execute_Result.md)
- [公式解析](../references/SDK_BestPractices.md) (IGenerateContext.EvaluateFormulaAsync)

## 4. 属性设计
- `WorkOrders`: `[FormulaProperty]` 类型 `object`。
- `SchedulingRule`: 枚举类型 `RuleType`。
- `ResultToVariableName`: `string` 类型，用于指定返回变量。

## 5. 逻辑实现细节
- 定义 `WorkOrder` 实体类用于反序列化。
- 在 `ExecuteAsync` 中：
    1. 解析 `WorkOrders` 公式。
    2. 将结果转换为 `List<WorkOrder>`。
    3. 根据 `SchedulingRule` 执行排序。
    4. 将结果存入 `context.Parameters`。
    5. 返回 `ExecuteResult`。
