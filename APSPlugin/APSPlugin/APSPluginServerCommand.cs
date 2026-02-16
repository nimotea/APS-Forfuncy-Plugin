using GrapeCity.Forguncy.Commands;
using GrapeCity.Forguncy.Plugin;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace APSPlugin
{
    public enum SchedulingRule
    {
        [Description("EDD - 最早交期优先")]
        EDD,
        [Description("SPT - 最短加工时间优先")]
        SPT,
        [Description("WSPT - 加权最短加工时间优先")]
        WSPT,
        [Description("CR - 临界比例规则")]
        CR,
        [Description("FIFO - 先来先服务")]
        FIFO
    }

    public enum InternalField
    {
        [Description("工单号 (OrderID)")]
        OrderID,
        [Description("加工时长 (ProcessingTime)")]
        ProcessingTime,
        [Description("交货日期 (DueDate)")]
        DueDate,
        [Description("订单优先级 (Priority)")]
        Priority,
        [Description("到达时间 (ArrivalTime)")]
        ArrivalTime,
        [Description("资源组 (ResourceGroupID)")]
        ResourceGroupID
    }

    public class FieldMappingItem : ObjectPropertyBase
    {
        [DisplayName("内部字段")]
        public InternalField InternalField { get; set; }

        [FormulaProperty]
        [DisplayName("外部字段名称")]
        public object ExternalFieldName { get; set; }

        public override string ToString()
        {
            return $"{InternalField} -> {ExternalFieldName}";
        }
    }

    public class ResourceGroup
    {
        public string ResourceGroupID { get; set; }
        public double DailyCapacity { get; set; } // 每日总工时 (如 8 小时)
        public double Efficiency { get; set; } = 1.0; // 效率 (0-1)
    }

    public class WorkOrder
    {
        public string OrderID { get; set; }
        public double ProcessingTime { get; set; }
        public DateTime DueDate { get; set; }
        public double Priority { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string ResourceGroupID { get; set; }
        public int SequenceNo { get; set; }
        
        // 排产结果字段
        public DateTime? ScheduledDate { get; set; }
        public string CapacityStatus { get; set; }
        public bool IsOverdue { get; set; }
        public double DelayDays { get; set; }
    }

    [Icon("pack://application:,,,/APSPlugin;component/Resources/Icon.png")]
    [Designer("APSPlugin.Designer.APSPluginServerCommandDesigner, APSPlugin")]
    [DisplayName("APS 粗能力排程 (L1.5)")]
    [Description("执行宏观产能平衡排程。支持资源组负荷计算、自动顺延与逾期预警。")]
    public class APSPluginServerCommand : Command, ICommandExecutableInServerSideAsync, IServerCommandParamGenerator
    {
        [FormulaProperty]
        [DisplayName("工单集合")]
        [Description("支持 JSON 字符串或对象列表。")]
        public object WorkOrders { get; set; }

        [FormulaProperty]
        [DisplayName("资源组配置")]
        [Description("定义资源组及其产能。JSON 格式：[{ \"ResourceGroupID\": \"CNC\", \"DailyCapacity\": 8, \"Efficiency\": 0.9 }]")]
        public object ResourceData { get; set; }

        [FormulaProperty]
        [DisplayName("排产开始日期")]
        [Description("排程计算的起始日期。默认为今天。")]
        public object StartDate { get; set; }

        [ListProperty]
        [DisplayName("字段名称映射")]
        [Description("配置输入数据中的字段名称映射。如果未配置，将尝试匹配默认名称（如“工单号”、“加工时长”等）。")]
        public List<FieldMappingItem> FieldMappings { get; set; } = new List<FieldMappingItem>();

        [DisplayName("排序规则")]
        [Description("选择用于初始排序的 APS 算法。")]
        public SchedulingRule Rule { get; set; } = SchedulingRule.EDD;

        [ResultToProperty]
        [DisplayName("将结果保存到变量")]
        [Description("排程后的工单集合将存储在此变量中。包含建议日期与负荷状态。")]
        public string ResultTo { get; set; } = "ScheduledWorkOrders";

        public IEnumerable<GenerateParam> GetGenerateParams()
        {
            yield return new GenerateListParam()
            {
                ParamName = this.ResultTo,
                Description = "排程后的工单集合",
                ItemProperties = new List<string> 
                { 
                    nameof(WorkOrder.OrderID), 
                    nameof(WorkOrder.ProcessingTime), 
                    nameof(WorkOrder.DueDate), 
                    nameof(WorkOrder.Priority), 
                    nameof(WorkOrder.ArrivalTime),
                    nameof(WorkOrder.ResourceGroupID),
                    nameof(WorkOrder.SequenceNo),
                    nameof(WorkOrder.ScheduledDate),
                    nameof(WorkOrder.CapacityStatus),
                    nameof(WorkOrder.IsOverdue),
                    nameof(WorkOrder.DelayDays)
                },
                ItemPropertiesDescription = new Dictionary<string, string>
                {
                    { nameof(WorkOrder.OrderID), "工单号" },
                    { nameof(WorkOrder.ProcessingTime), "加工时长" },
                    { nameof(WorkOrder.DueDate), "交货日期" },
                    { nameof(WorkOrder.Priority), "订单优先级" },
                    { nameof(WorkOrder.ArrivalTime), "到达时间" },
                    { nameof(WorkOrder.ResourceGroupID), "资源组ID" },
                    { nameof(WorkOrder.SequenceNo), "排程序号" },
                    { nameof(WorkOrder.ScheduledDate), "建议生产日期" },
                    { nameof(WorkOrder.CapacityStatus), "产能状态" },
                    { nameof(WorkOrder.IsOverdue), "是否逾期" },
                    { nameof(WorkOrder.DelayDays), "逾期天数" }
                }
            };
        }

        public async Task<ExecuteResult> ExecuteAsync(IServerCommandExecuteContext dataContext)
        {
            try
            {
                // 1. 获取并解析工单数据
                var rawWorkOrders = await dataContext.EvaluateFormulaAsync(WorkOrders);
                if (rawWorkOrders == null)
                {
                    return new ExecuteResult { Message = "工单集合不能为空。" };
                }

                // 2. 获取资源数据
                var rawResourceData = await dataContext.EvaluateFormulaAsync(ResourceData);
                List<ResourceGroup> resources = new List<ResourceGroup>();
                if (rawResourceData != null)
                {
                    string resJson = rawResourceData is string s ? s : JsonConvert.SerializeObject(rawResourceData);
                    resources = JsonConvert.DeserializeObject<List<ResourceGroup>>(resJson) ?? new List<ResourceGroup>();
                }
                var resourceDict = resources.ToDictionary(r => r.ResourceGroupID, r => r);

                // 3. 获取开始日期
                var rawStartDate = await dataContext.EvaluateFormulaAsync(StartDate);
                DateTime planStartDate = DateTime.Today;
                if (rawStartDate != null)
                {
                    if (rawStartDate is DateTime dt) planStartDate = dt;
                    else if (DateTime.TryParse(rawStartDate.ToString(), out DateTime dtParsed)) planStartDate = dtParsed;
                }

                // 4. 准备字段映射字典
                var mappingDict = new Dictionary<InternalField, string>();
                if (FieldMappings != null)
                {
                    foreach (var item in FieldMappings)
                    {
                        var externalName = (await dataContext.EvaluateFormulaAsync(item.ExternalFieldName))?.ToString();
                        if (!string.IsNullOrEmpty(externalName))
                        {
                            mappingDict[item.InternalField] = externalName;
                        }
                    }
                }

                // 默认映射补全
                if (!mappingDict.ContainsKey(InternalField.OrderID)) mappingDict[InternalField.OrderID] = "工单号";
                if (!mappingDict.ContainsKey(InternalField.ProcessingTime)) mappingDict[InternalField.ProcessingTime] = "加工时长";
                if (!mappingDict.ContainsKey(InternalField.DueDate)) mappingDict[InternalField.DueDate] = "交货日期";
                if (!mappingDict.ContainsKey(InternalField.Priority)) mappingDict[InternalField.Priority] = "订单优先级";
                if (!mappingDict.ContainsKey(InternalField.ArrivalTime)) mappingDict[InternalField.ArrivalTime] = "到达时间";
                if (!mappingDict.ContainsKey(InternalField.ResourceGroupID)) mappingDict[InternalField.ResourceGroupID] = "资源组ID";

                // 5. 提取数据
                List<JObject> jOrders;
                if (rawWorkOrders is string jsonStr)
                {
                    jOrders = JsonConvert.DeserializeObject<List<JObject>>(jsonStr);
                }
                else
                {
                    var json = JsonConvert.SerializeObject(rawWorkOrders);
                    jOrders = JsonConvert.DeserializeObject<List<JObject>>(json);
                }

                if (jOrders == null || !jOrders.Any())
                {
                    dataContext.Parameters[ResultTo] = rawWorkOrders;
                    return new ExecuteResult();
                }

                var orderPairs = new List<(WorkOrder Order, JObject Result)>();
                foreach (var jo in jOrders)
                {
                    var order = new WorkOrder();
                    var resultJo = (JObject)jo.DeepClone(); // 克隆一份用于返回结果
                    
                    // 提取并标准化各个核心字段
                    string extOrderID = mappingDict[InternalField.OrderID];
                    string extProcessingTime = mappingDict[InternalField.ProcessingTime];
                    string extDueDate = mappingDict[InternalField.DueDate];
                    string extPriority = mappingDict[InternalField.Priority];
                    string extArrivalTime = mappingDict[InternalField.ArrivalTime];
                    string extResourceGroupID = mappingDict[InternalField.ResourceGroupID];

                    order.OrderID = jo[extOrderID]?.ToString() ?? "N/A";
                    order.ProcessingTime = ParseDouble(jo[extProcessingTime], 0);
                    order.DueDate = ParseDateTime(jo[extDueDate], DateTime.MaxValue);
                    order.Priority = ParseDouble(jo[extPriority], 1.0);
                    order.ArrivalTime = ParseDateTime(jo[extArrivalTime], DateTime.Now);
                    order.ResourceGroupID = jo[extResourceGroupID]?.ToString();

                    // 映射回结果对象
                    UpdateResultField(resultJo, extOrderID, nameof(WorkOrder.OrderID));
                    UpdateResultField(resultJo, extProcessingTime, nameof(WorkOrder.ProcessingTime));
                    UpdateResultField(resultJo, extDueDate, nameof(WorkOrder.DueDate));
                    UpdateResultField(resultJo, extPriority, nameof(WorkOrder.Priority));
                    UpdateResultField(resultJo, extArrivalTime, nameof(WorkOrder.ArrivalTime));
                    UpdateResultField(resultJo, extResourceGroupID, nameof(WorkOrder.ResourceGroupID));

                    orderPairs.Add((order, resultJo));
                }

                // 6. 执行规则校验
                string validationError = ValidateData(orderPairs.Select(p => p.Order).ToList(), Rule);
                if (!string.IsNullOrEmpty(validationError))
                {
                    return new ExecuteResult { Message = validationError };
                }

                // 7. 执行排序逻辑 (Step 1: Sorting)
                IEnumerable<(WorkOrder Order, JObject Result)> sortedPairs;
                switch (Rule)
                {
                    case SchedulingRule.EDD:
                        sortedPairs = orderPairs.OrderBy(p => p.Order.DueDate);
                        break;
                    case SchedulingRule.SPT:
                        sortedPairs = orderPairs.OrderBy(p => p.Order.ProcessingTime);
                        break;
                    case SchedulingRule.WSPT:
                        sortedPairs = orderPairs.OrderByDescending(p => p.Order.ProcessingTime == 0 ? 0 : p.Order.Priority / p.Order.ProcessingTime);
                        break;
                    case SchedulingRule.CR:
                        var now = DateTime.Now;
                        sortedPairs = orderPairs.OrderBy(p =>
                        {
                            var remainingTime = (p.Order.DueDate - now).TotalHours;
                            return p.Order.ProcessingTime == 0 ? double.MaxValue : remainingTime / p.Order.ProcessingTime;
                        });
                        break;
                    case SchedulingRule.FIFO:
                        sortedPairs = orderPairs.OrderBy(p => p.Order.ArrivalTime);
                        break;
                    default:
                        sortedPairs = orderPairs;
                        break;
                }

                // 8. 执行 RCCP 排程 (Step 2: Filling Buckets)
                // 资源桶：ResourceID -> Date -> UsedHours
                var resourceBuckets = new Dictionary<string, Dictionary<DateTime, double>>();
                var resultList = new List<JObject>();
                int sequence = 1;

                foreach (var pair in sortedPairs)
                {
                    var order = pair.Order;
                    var jo = pair.Result;
                    
                    jo[nameof(WorkOrder.SequenceNo)] = sequence++;

                    // 如果未指定资源组或资源组不存在，无法排程，标记为异常
                    if (string.IsNullOrEmpty(order.ResourceGroupID) || !resourceDict.ContainsKey(order.ResourceGroupID))
                    {
                        jo[nameof(WorkOrder.CapacityStatus)] = "Unknown Resource";
                        resultList.Add(jo);
                        continue;
                    }

                    var resource = resourceDict[order.ResourceGroupID];
                    double realCapacity = resource.DailyCapacity * resource.Efficiency;
                    
                    if (realCapacity <= 0)
                    {
                        jo[nameof(WorkOrder.CapacityStatus)] = "Zero Capacity";
                        resultList.Add(jo);
                        continue;
                    }

                    // 寻找最早可用日期 (Forward Scheduling)
                    // 从 Max(PlanStartDate, ArrivalTime) 开始
                    DateTime searchDate = planStartDate > order.ArrivalTime ? planStartDate : order.ArrivalTime;
                    searchDate = searchDate.Date; // 取整到日期

                    bool scheduled = false;
                    // 防止死循环，设定一个最大搜索天数（例如 365 天）
                    for (int i = 0; i < 365; i++)
                    {
                        if (!resourceBuckets.ContainsKey(order.ResourceGroupID))
                        {
                            resourceBuckets[order.ResourceGroupID] = new Dictionary<DateTime, double>();
                        }
                        var buckets = resourceBuckets[order.ResourceGroupID];
                        
                        if (!buckets.ContainsKey(searchDate))
                        {
                            buckets[searchDate] = 0;
                        }

                        double currentLoad = buckets[searchDate];
                        
                        // 策略：如果当天剩余产能足够放下整个工单（或工单允许跨天但简化为只要有空闲就排入主要部分）
                        // L1.5 简化策略：只要当天还没满（Load < Capacity），就尝试放入。
                        // 如果工单工时 > 当天剩余工时，这里我们简单地将工单“归属”于这一天，并标记该天过载（Overload），或者顺延到哪一天能完全放下？
                        // 根据文档：“当某日产能已满，排序靠后的工单应自动标记为‘建议顺延至次日’”
                        // 这意味着：必须找到一天，其 (CurrentLoad + OrderTime) <= Capacity。
                        // 但是，如果 OrderTime > Capacity，则永远无法满足。
                        // 修正策略：如果 OrderTime > Capacity，则找一个由空闲的日期放入，并标记过载。
                        // 正常情况：找一个能放下的日期。

                        if (currentLoad + order.ProcessingTime <= realCapacity)
                        {
                            // Found a valid slot
                            buckets[searchDate] += order.ProcessingTime;
                            order.ScheduledDate = searchDate;
                            order.CapacityStatus = "Normal";
                            scheduled = true;
                            break;
                        }
                        else if (currentLoad == 0 && order.ProcessingTime > realCapacity)
                        {
                            // 特殊情况：工单本身就比单日产能大，且当天是空的。
                            // 强制放入，并标记过载。
                            buckets[searchDate] += order.ProcessingTime;
                            order.ScheduledDate = searchDate;
                            order.CapacityStatus = "Overloaded (Job too large)";
                            scheduled = true;
                            break;
                        }

                        // 否则，顺延至下一天
                        searchDate = searchDate.AddDays(1);
                    }

                    if (!scheduled)
                    {
                        order.CapacityStatus = "Failed (No Slot)";
                    }
                    else
                    {
                        // 计算逾期
                        if (order.ScheduledDate.Value > order.DueDate)
                        {
                            order.IsOverdue = true;
                            order.DelayDays = (order.ScheduledDate.Value - order.DueDate).TotalDays;
                            // 如果已经是 Overloaded，保留 Overloaded 状态，或者叠加？
                            // 文档要求：CapacityStatus（产能状态：正常/过载/结余）。
                            // 这里我们用 CapacityStatus 描述当天的负荷情况。
                            // 如果当天最终 Load > Capacity，则为 Overloaded。
                            // 但由于我们是逐个排的，上面的逻辑保证了只要能放下就不超，除非单工单超限。
                            // 所以这里的 CapacityStatus 主要反映单工单超限。
                            // 而“结余”很难在单工单上体现，除非是指该工单排进去后，当天还有剩？
                        }
                        else
                        {
                            order.IsOverdue = false;
                            order.DelayDays = 0;
                        }
                    }

                    // 回填结果
                    jo[nameof(WorkOrder.ScheduledDate)] = order.ScheduledDate;
                    jo[nameof(WorkOrder.CapacityStatus)] = order.CapacityStatus;
                    jo[nameof(WorkOrder.IsOverdue)] = order.IsOverdue;
                    jo[nameof(WorkOrder.DelayDays)] = order.DelayDays;
                    
                    resultList.Add(jo);
                }

                dataContext.Parameters[ResultTo] = resultList;
                return new ExecuteResult();
            }
            catch (Exception ex)
            {
                return new ExecuteResult
                {
                    Message = $"排程排序执行失败: {ex.Message}"
                };
            }
        }

        private void UpdateResultField(JObject jo, string externalName, string internalName)
        {
            if (string.IsNullOrEmpty(externalName) || externalName == internalName) return;

            // 如果原始数据中有外部映射名称的字段
            if (jo.TryGetValue(externalName, out JToken value))
            {
                // 设置内部标准名称的值
                jo[internalName] = value;
                // 移除原始的外部名称字段（避免冗余）
                jo.Remove(externalName);
            }
        }

        private double ParseDouble(JToken token, double defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            try
            {
                return token.Value<double>();
            }
            catch
            {
                return defaultValue;
            }
        }

        private DateTime ParseDateTime(JToken token, DateTime defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null) return defaultValue;

            // 处理 OADate (活字格内建表日期格式)
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                try
                {
                    double oaDate = token.Value<double>();
                    return DateTime.FromOADate(oaDate);
                }
                catch
                {
                    return defaultValue;
                }
            }

            // 处理标准日期格式
            try
            {
                return token.Value<DateTime>();
            }
            catch
            {
                // 尝试解析字符串
                if (DateTime.TryParse(token.ToString(), out DateTime result))
                {
                    return result;
                }
                return defaultValue;
            }
        }

        private string ValidateData(List<WorkOrder> orders, SchedulingRule rule)
        {
            if (rule == SchedulingRule.EDD || rule == SchedulingRule.CR)
            {
                if (orders.Any(o => o.DueDate == DateTime.MaxValue))
                {
                    return $"使用 {rule} 规则时，所有工单必须包含有效的“交货日期”。";
                }
            }
            if (rule == SchedulingRule.SPT || rule == SchedulingRule.WSPT || rule == SchedulingRule.CR)
            {
                if (orders.Any(o => o.ProcessingTime <= 0))
                {
                    return $"使用 {rule} 规则时，所有工单必须包含大于 0 的“加工时长”。";
                }
            }
            return null;
        }

        public override string ToString()
        {
            var mappingStatus = FieldMappings != null && FieldMappings.Any() ? " (自定义映射)" : "";
            return $"APS 排程排序: {Rule}{mappingStatus}";
        }

        public override CommandScope GetCommandScope()
        {
            return CommandScope.ExecutableInServer;
        }
    }
}