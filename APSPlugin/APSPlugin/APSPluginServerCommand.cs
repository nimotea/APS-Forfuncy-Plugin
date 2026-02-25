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
        ResourceGroupID,
        [Description("工序序号 (SequenceNo)")]
        SequenceNo
    }

    public enum ResourceField
    {
        [Description("设备编号 (ResourceID)")]
        ResourceID,
        [Description("资源组编号 (ResourceGroupID)")]
        ResourceGroupID,
        [Description("标准产能 (StandardCapacity)")]
        StandardCapacity
    }

    public enum CalendarField
    {
        [Description("设备编号 (ResourceID)")]
        ResourceID,
        [Description("例外日期 (ExceptionDate)")]
        ExceptionDate,
        [Description("产能变更 (ChangeValue)")]
        ChangeValue
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

    public class ResourceMappingItem : ObjectPropertyBase
    {
        [DisplayName("资源字段")]
        public ResourceField ResourceField { get; set; }

        [FormulaProperty]
        [DisplayName("数据列名")]
        public object ColumnName { get; set; }

        public override string ToString()
        {
            return $"{ResourceField} -> {ColumnName}";
        }
    }

    public class CalendarMappingItem : ObjectPropertyBase
    {
        [DisplayName("日历字段")]
        public CalendarField CalendarField { get; set; }

        [FormulaProperty]
        [DisplayName("数据列名")]
        public object ColumnName { get; set; }

        public override string ToString()
        {
            return $"{CalendarField} -> {ColumnName}";
        }
    }

    public class ResourceGroup
    {
        [JsonProperty("ResourceGroupID")]
        public string ResourceGroupID { get; set; }

        [JsonProperty("Efficiency")]
        public double Efficiency { get; set; } = 1.0; // 效率 (0-1)
    }

    public class ResourceItem
    {
        [JsonProperty("ResourceID")]
        public string ResourceID { get; set; }

        [JsonProperty("ResourceGroupID")]
        public string ResourceGroupID { get; set; }

        [JsonProperty("StandardCapacity")]
        public double StandardCapacity { get; set; }
    }

    public class CalendarException
    {
        [JsonProperty("ResourceID")]
        public string ResourceID { get; set; }

        [JsonProperty("ExceptionDate")]
        public DateTime ExceptionDate { get; set; }

        [JsonProperty("ChangeValue")]
        public double ChangeValue { get; set; } // 产能变更值 (例如 -8 表示停机，+2 表示加班)
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

    [Icon("pack://application:,,,/APSPluginAdvanced;component/Resources/Icon.png")]
    [Designer("APSPlugin.Designer.APSPluginServerCommandDesigner, APSPluginAdvanced")]
    [DisplayName("APS 高级排程 (L1.5)")]
    [Description("执行高级宏观产能平衡排程。支持资源组负荷计算、自动顺延与逾期预警。")]
    public class APSPluginServerCommand : Command, ICommandExecutableInServerSideAsync, IServerCommandParamGenerator
    {
        [FormulaProperty]
        [DisplayName("工单集合")]
        [Description("支持 JSON 字符串或对象列表。")]
        public object WorkOrders { get; set; }

        [FormulaProperty]
        [DisplayName("资源组配置")]
        [Description("定义资源组及其效率。JSON 格式：[{ \"ResourceGroupID\": \"CNC\", \"Efficiency\": 0.9 }]")]
        public object ResourceData { get; set; }

        [FormulaProperty]
        [DisplayName("日历例外")]
        [Description("支持 JSON 数组字符串或表格数据。请配合下方的字段映射使用。")]
        public object CalendarExceptions { get; set; }

        [ListProperty]
        [DisplayName("日历字段映射")]
        [Description("配置日历例外数据的列名映射。")]
        public List<CalendarMappingItem> CalendarMappings { get; set; } = new List<CalendarMappingItem>
        {
            new CalendarMappingItem { CalendarField = CalendarField.ResourceID, ColumnName = "设备编号" },
            new CalendarMappingItem { CalendarField = CalendarField.ExceptionDate, ColumnName = "例外日期" },
            new CalendarMappingItem { CalendarField = CalendarField.ChangeValue, ColumnName = "产能变更" }
        };

        [FormulaProperty]
        [DisplayName("资源明细")]
        [Description("支持 JSON 数组字符串或表格数据。请配合下方的字段映射使用。")]
        public object ResourceList { get; set; }

        [ListProperty]
        [DisplayName("资源字段映射")]
        [Description("配置资源明细数据的列名映射。")]
        public List<ResourceMappingItem> ResourceMappings { get; set; } = new List<ResourceMappingItem>
        {
            new ResourceMappingItem { ResourceField = ResourceField.ResourceID, ColumnName = "设备编号" },
            new ResourceMappingItem { ResourceField = ResourceField.ResourceGroupID, ColumnName = "资源组编号" },
            new ResourceMappingItem { ResourceField = ResourceField.StandardCapacity, ColumnName = "标准产能" }
        };

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
                
                // 2. 获取资源组配置
                var rawResourceData = await dataContext.EvaluateFormulaAsync(ResourceData);
                var resources = Deserialize<List<ResourceGroup>>(rawResourceData) ?? new List<ResourceGroup>();
                var groupDict = resources.ToDictionary(r => r.ResourceGroupID, r => r);

                // 3. 获取资源明细 (JSON 或 表格)
                List<ResourceItem> resourceItems = new List<ResourceItem>();
                int skippedResources = 0;
                var rawResourceList = await dataContext.EvaluateFormulaAsync(ResourceList);
                if (rawResourceList != null)
                {
                    List<JObject> jItems = null;
                    if (rawResourceList is string resourceJsonStr)
                    {
                        jItems = JsonConvert.DeserializeObject<List<JObject>>(resourceJsonStr);
                    }
                    else
                    {
                        var json = JsonConvert.SerializeObject(rawResourceList);
                        jItems = JsonConvert.DeserializeObject<List<JObject>>(json);
                    }

                    if (jItems != null)
                    {
                        var resMap = new Dictionary<ResourceField, string>();
                        if (ResourceMappings != null)
                        {
                            foreach (var m in ResourceMappings)
                            {
                                var col = (await dataContext.EvaluateFormulaAsync(m.ColumnName))?.ToString();
                                if (!string.IsNullOrEmpty(col)) resMap[m.ResourceField] = col;
                            }
                        }
                        foreach (var row in jItems)
                        {
                            var item = new ResourceItem();
                            if (resMap.ContainsKey(ResourceField.ResourceID)) 
                                item.ResourceID = row[resMap[ResourceField.ResourceID]]?.ToString();
                            
                            if (resMap.ContainsKey(ResourceField.ResourceGroupID)) 
                                item.ResourceGroupID = row[resMap[ResourceField.ResourceGroupID]]?.ToString();
                            
                            if (resMap.ContainsKey(ResourceField.StandardCapacity))
                            {
                                var val = row[resMap[ResourceField.StandardCapacity]];
                                item.StandardCapacity = ParseDouble(val, 0);
                            }
                            
                            // 关键修复：忽略没有资源组ID或资源ID的无效数据
                            if (!string.IsNullOrEmpty(item.ResourceGroupID) && !string.IsNullOrEmpty(item.ResourceID))
                            {
                                resourceItems.Add(item);
                            }
                            else
                            {
                                skippedResources++;
                            }
                        }
                    }
                }
                var resourcesByGroup = resourceItems.GroupBy(r => r.ResourceGroupID).ToDictionary(g => g.Key, g => g.ToList());

                // 4. 获取日历例外 (JSON 或 表格)
                List<CalendarException> exceptionItems = new List<CalendarException>();
                int skippedExceptions = 0;
                var rawExceptions = await dataContext.EvaluateFormulaAsync(CalendarExceptions);
                if (rawExceptions != null)
                {
                    List<JObject> jItems = null;
                    if (rawExceptions is string calendarJsonStr)
                    {
                        jItems = JsonConvert.DeserializeObject<List<JObject>>(calendarJsonStr);
                    }
                    else
                    {
                        var json = JsonConvert.SerializeObject(rawExceptions);
                        jItems = JsonConvert.DeserializeObject<List<JObject>>(json);
                    }

                    if (jItems != null)
                    {
                        var calMap = new Dictionary<CalendarField, string>();
                        if (CalendarMappings != null)
                        {
                            foreach (var m in CalendarMappings)
                            {
                                var col = (await dataContext.EvaluateFormulaAsync(m.ColumnName))?.ToString();
                                if (!string.IsNullOrEmpty(col)) calMap[m.CalendarField] = col;
                            }
                        }
                        foreach (var row in jItems)
                        {
                            var item = new CalendarException();
                            if (calMap.ContainsKey(CalendarField.ResourceID)) 
                                item.ResourceID = row[calMap[CalendarField.ResourceID]]?.ToString();
                            
                            if (calMap.ContainsKey(CalendarField.ExceptionDate)) 
                                item.ExceptionDate = ParseDateTime(row[calMap[CalendarField.ExceptionDate]], DateTime.MinValue);
                            
                            if (calMap.ContainsKey(CalendarField.ChangeValue))
                            {
                                var val = row[calMap[CalendarField.ChangeValue]];
                                item.ChangeValue = ParseDouble(val, 0);
                            }
                            
                            // 关键修复：忽略没有资源ID的无效数据
                            if (!string.IsNullOrEmpty(item.ResourceID))
                            {
                                exceptionItems.Add(item);
                            }
                            else
                            {
                                skippedExceptions++;
                            }
                        }
                    }
                }
                // ResourceID -> Date -> TotalChange
                var exceptionsMap = exceptionItems
                    .GroupBy(e => e.ResourceID)
                    .ToDictionary(g => g.Key, g => g.GroupBy(e => e.ExceptionDate.Date).ToDictionary(d => d.Key, d => d.Sum(x => x.ChangeValue)));

                // 5. 获取开始日期
                var rawStartDate = await dataContext.EvaluateFormulaAsync(StartDate);
                DateTime planStartDate = DateTime.Today;
                if (rawStartDate != null)
                {
                    if (rawStartDate is DateTime dt) planStartDate = dt;
                    else if (DateTime.TryParse(rawStartDate.ToString(), out DateTime dtParsed)) planStartDate = dtParsed;
                }

                // 6. 准备字段映射字典
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

                // 7. 提取数据
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

                // 8. 执行规则校验
                string validationError = ValidateData(orderPairs.Select(p => p.Order).ToList(), Rule);
                if (!string.IsNullOrEmpty(validationError))
                {
                    return new ExecuteResult { Message = validationError };
                }

                // 9. 执行排序逻辑 (Step 1: Sorting)
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

                // 10. 执行 RCCP 排程 (Step 2: Filling Buckets)
                // 资源桶：ResourceID -> Date -> UsedHours
                var resourceBuckets = new Dictionary<string, Dictionary<DateTime, double>>();
                var resultList = new List<JObject>();
                int sequence = 1;

                // 产能计算辅助函数
                double GetDailyCapacity(string groupId, DateTime date)
                {
                    // 如果该组没有资源明细，返回 0
                    if (!resourcesByGroup.ContainsKey(groupId)) return 0;

                    // 获取组效率（如果未定义，默认为 1.0）
                    double efficiency = 1.0;
                    if (groupDict.ContainsKey(groupId))
                    {
                        efficiency = groupDict[groupId].Efficiency;
                    }

                    double totalCapacity = 0;
                    foreach (var res in resourcesByGroup[groupId])
                    {
                        double cap = res.StandardCapacity;
                        // 应用例外
                        if (exceptionsMap.TryGetValue(res.ResourceID, out var dateMap) && dateMap.TryGetValue(date.Date, out double change))
                        {
                            cap += change;
                        }
                        if (cap < 0) cap = 0; // 单设备产能不为负
                        totalCapacity += cap;
                    }

                    return totalCapacity * efficiency;
                }

                foreach (var pair in sortedPairs)
                {
                    var order = pair.Order;
                    var resultJo = pair.Result;
                    
                    resultJo[nameof(WorkOrder.SequenceNo)] = sequence++;

                    // 如果未指定资源组或资源组不存在，无法排程
                    // 修正逻辑：只要有资源明细，就算有效资源组
                    if (string.IsNullOrEmpty(order.ResourceGroupID) || !resourcesByGroup.ContainsKey(order.ResourceGroupID))
                    {
                        string invalidId = string.IsNullOrEmpty(order.ResourceGroupID) ? "(Empty)" : order.ResourceGroupID;
                        resultJo[nameof(WorkOrder.CapacityStatus)] = $"Unknown Resource: {invalidId}";
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
                        // 获取当天的实际产能 (动态计算)
                        double dailyCapacity = GetDailyCapacity(order.ResourceGroupID, searchDate);

                        if (dailyCapacity <= 0)
                        {
                            // 当天无产能（休息日或停机），直接跳过
                            searchDate = searchDate.AddDays(1);
                            continue;
                        }

                        if (!resourceBuckets.ContainsKey(order.ResourceGroupID))
                        {
                            resourceBuckets[order.ResourceGroupID] = new Dictionary<DateTime, double>();
                        }
                        var bucket = resourceBuckets[order.ResourceGroupID];

                        // 计算当天剩余产能
                        double used = bucket.ContainsKey(searchDate) ? bucket[searchDate] : 0;
                        double remaining = dailyCapacity - used;

                        if (remaining >= order.ProcessingTime)
                        {
                            // 产能足够，安排在今天
                            bucket[searchDate] = used + order.ProcessingTime;
                            
                            order.ScheduledDate = searchDate;
                            order.CapacityStatus = "Scheduled";
                            order.IsOverdue = searchDate > order.DueDate;
                            order.DelayDays = order.IsOverdue ? (searchDate - order.DueDate).TotalDays : 0;
                            
                            scheduled = true;
                            break;
                        }
                        else if (used == 0 && order.ProcessingTime > dailyCapacity)
                        {
                            // 特殊情况：工单本身耗时超过单日总产能，且当天为空
                            // 策略：允许强行安排在当天（视为开始日期），实际会跨天，但在 L1.5 模型中简化为占满当天。
                            bucket[searchDate] = order.ProcessingTime; // 记录实际负载，可能 > dailyCapacity

                            order.ScheduledDate = searchDate;
                            order.CapacityStatus = "Scheduled (Overloaded)";
                            order.IsOverdue = searchDate > order.DueDate;
                            order.DelayDays = order.IsOverdue ? (searchDate - order.DueDate).TotalDays : 0;

                            scheduled = true;
                            break;
                        }
                        else
                        {
                            // 产能不足，推迟到下一天
                            searchDate = searchDate.AddDays(1);
                        }
                    }

                    if (!scheduled)
                    {
                        order.CapacityStatus = "Capacity Overflow";
                    }

                    // 更新结果对象
                    if (order.ScheduledDate.HasValue)
                        resultJo[nameof(WorkOrder.ScheduledDate)] = order.ScheduledDate.Value.ToString("yyyy-MM-dd");
                    
                    resultJo[nameof(WorkOrder.CapacityStatus)] = order.CapacityStatus;
                    resultJo[nameof(WorkOrder.IsOverdue)] = order.IsOverdue;
                    resultJo[nameof(WorkOrder.DelayDays)] = order.DelayDays;
                }

                dataContext.Parameters[ResultTo] = orderPairs.Select(p => p.Result).ToList();
                return new ExecuteResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[APSPlugin] [CRITICAL] 发生未处理异常: {ex.Message}\n{ex.StackTrace}");
                return new ExecuteResult { Message = $"排程执行失败: {ex.Message}" };
            }
        }

        private DateTime ParseDateTime(object obj, DateTime defaultVal)
        {
            if (obj == null) return defaultVal;
            if (DateTime.TryParse(obj.ToString(), out var dt)) return dt;
            return defaultVal;
        }

        private double ParseDouble(object obj, double defaultVal)
        {
            if (obj == null) return defaultVal;
            if (double.TryParse(obj.ToString(), out var d)) return d;
            return defaultVal;
        }

        private T Deserialize<T>(object input)
        {
            if (input == null) return default(T);
            try
            {
                return JsonConvert.DeserializeObject<T>(input.ToString());
            }
            catch
            {
                return default(T);
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