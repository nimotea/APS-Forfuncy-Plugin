# APS L1.5 (RCCP) 演示工程表结构设计

本设计旨在支持 APS L1.5 插件的 **粗能力计划 (RCCP)** 特性，重点在于资源组的产能平衡与交期预测。

## 1. 基础数据层 (Master Data)

定义工厂的生产能力资源池。L1.5 阶段的核心是**资源组 (Resource Group)**，而非独立机台。

### 1.1 资源组产能表 (ResourceGroup_Master)
**对应插件参数**：`ResourceData` (JSON)
定义各工序/车间的**默认效率**。
*   注意：`DailyCapacity` 字段在此模式下可能不再直接使用，而是由插件根据 `Resource_Master` 自动聚合。但在简化模式下仍可作为默认值。

| 字段名 | 字段类型 | 说明 | 示例 | 插件映射 |
| :--- | :--- | :--- | :--- | :--- |
| ResourceGroupID | 文本 (PK) | 资源组唯一标识 | CNC_CENTER | `ResourceGroupID` |
| GroupName | 文本 | 资源组名称 | 数控加工中心 | - |
| Efficiency | 小数 | 综合效率 (0.0-1.0) | 0.9 | `Efficiency` |
| Description | 文本 | 备注 | 包含3台五轴机床 | - |

### 1.2 资源设备表 (Resource_Master) - Updated!
**对应插件参数**：`ResourceList` (JSON List) - **新增参数**
定义每个资源组下的具体设备清单及其标准产能。

| 字段名 | 字段类型 | 说明 | 示例 | 插件映射 |
| :--- | :--- | :--- | :--- | :--- |
| ResourceID | 文本 (PK) | 设备编号 | MC-001 | `ResourceID` |
| ResourceName | 文本 | 设备名称 | 5轴加工中心-1号 | - |
| ResourceGroupID | 文本 (FK) | 所属资源组 | CNC_CENTER | `ResourceGroupID` |
| StandardCapacity | 小数 | **标准日产能** (小时) | 24 | `StandardCapacity` |
| Status | 选项 | 设备状态 | 正常 / 停用 | - |

### 1.3 资源日历例外表 (Resource_Calendar) - Refactored!
**对应插件参数**：`CalendarExceptions` (JSON List)
**作用**：记录**单台设备**的特殊状态（检修、加班等）。
*   插件会自动聚合：`某日资源组总产能 = Σ(单台设备当日产能)`。
*   如果设备在本日历表无记录，则取 `Resource_Master.StandardCapacity`。

| 字段名 | 字段类型 | 说明 | 示例 | 插件映射 |
| :--- | :--- | :--- | :--- | :--- |
| CalendarID | 自动编号 | 主键 | 1 | - |
| ResourceID | 文本 (FK) | **设备编号** | MC-001 | `ResourceID` |
| ExceptionDate | 日期 | **例外日期** | 2026-02-18 | `Date` |
| ExceptionType | 选项 | 类型 | 检修 / 加班 | `Type` |
| OverrideCapacity | 小数 | **当日实际产能** | 0 (检修) | `Value` |
| Comments | 文本 | 说明 | 故障维修 | - |

---

## 2. 业务需求层 (Transactional Data)

存储待排产的任务清单。

### 2.1 生产工单表 (WorkOrder_List)
**对应插件参数**：`WorkOrders` (JSON)

| 字段名 | 字段类型 | 说明 | 示例 | 插件映射 |
| :--- | :--- | :--- | :--- | :--- |
| WONumber | 文本 (PK) | 工单号 | WO20260213001 | `OrderID` |
| ProductName | 文本 | 产品名称 | 铝合金支架 | - |
| ResourceGroupID | 文本 (FK) | **所需资源组** (核心字段) | CNC_CENTER | `ResourceGroupID` |
| ProcessingTime | 小数 | **加工时长** (小时) | 4.5 | `ProcessingTime` |
| DueDate | 日期时间 | **交货日期** | 2026-02-20 | `DueDate` |
| Priority | 整数 | **权重** (1-10) | 1 | `Priority` |
| ArrivalTime | 日期时间 | **到达/释放时间** | 2026-02-13 09:00 | `ArrivalTime` |
| OrderStatus | 选项 | 状态 | 待排产 / 排程中 | - |

> **注意**：L1.5 插件会根据 `ArrivalTime` 和当前时间决定最早可排程日期。

---

## 3. 排程结果层 (Scheduling Output)

存储插件计算后的排程方案与详细结果。

### 3.1 排程方案记录表 (Schedule_Plan)
记录每次排程的执行元数据。

| 字段名 | 字段类型 | 说明 | 示例 |
| :--- | :--- | :--- | :--- |
| PlanID | 文本 (PK) | 方案编号 | PLAN-20260216-001 |
| RuleUsed | 选项 | 排序规则 | EDD / WSPT / CR |
| StartDate | 日期 | 排程计算开始日期 | 2026-02-16 |
| CreatedBy | 文本 | 操作员 | 张工 |
| CreateTime | 日期时间 | 执行时间 | 2026-02-16 14:00 |

### 3.2 排程详情表 (Schedule_Details)
**对应插件输出**：`ScheduledWorkOrders` (JSON List)
存储具体的排程结果。

| 字段名 | 字段类型 | 说明 | 示例 | 插件映射 |
| :--- | :--- | :--- | :--- | :--- |
| DetailID | 自动编号 | 主键 | 1 | - |
| PlanID | 文本 (FK) | 关联方案编号 | PLAN-20260216-001 | - |
| WONumber | 文本 | 工单号 | WO20260213001 | `OrderID` |
| SequenceNo | 整数 | **建议执行顺序** | 1 | `SequenceNo` |
| ScheduledDate | 日期 | **建议生产日期** (RCCP结果) | 2026-02-16 | `ScheduledDate` |
| CapacityStatus | 文本 | **产能状态** | Normal / Overloaded | `CapacityStatus` |
| IsOverdue | 布尔 | **是否逾期** | False | `IsOverdue` |
| DelayDays | 小数 | **逾期天数** | 0 | `DelayDays` |
| ResourceGroupID | 文本 | 分配的资源组 | CNC_CENTER | `ResourceGroupID` |

---

## 4. 视图设计建议 (View Design)

为了更直观地展示 RCCP 结果，建议在活字格中创建以下视图：

### 4.1 资源负载甘特图 (Resource Load View)
*   **X轴**：日期 (Date)
*   **Y轴**：资源组 (ResourceGroup)
*   **数据**：统计每日的 `Sum(ProcessingTime)` vs `DailyCapacity`。
*   **用途**：直观显示哪些天、哪些资源组产能过载（红色预警）。

### 4.2 订单交付分析 (Order Delivery Analysis)
*   **列表展示**：工单号、客户交期、建议生产日期、逾期天数。
*   **条件格式**：`IsOverdue = True` 的行高亮显示。
