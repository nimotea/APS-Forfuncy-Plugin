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
        ArrivalTime
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

    public class WorkOrder
    {
        public string OrderID { get; set; }
        public double ProcessingTime { get; set; }
        public DateTime DueDate { get; set; }
        public double Priority { get; set; }
        public DateTime ArrivalTime { get; set; }
        public int SequenceNo { get; set; }
    }

    [Icon("pack://application:,,,/APSPlugin;component/Resources/Icon.png")]
    [Designer("APSPlugin.Designer.APSPluginServerCommandDesigner, APSPlugin")]
    [DisplayName("APS 工单排程排序")]
    [Description("根据选定的 APS 算法规则对工单集合进行排序。支持动态字段映射与 OADate 格式。")]
    public class APSPluginServerCommand : Command, ICommandExecutableInServerSideAsync, IServerCommandParamGenerator
    {
        [FormulaProperty]
        [DisplayName("工单集合")]
        [Description("支持 JSON 字符串或对象列表。")]
        public object WorkOrders { get; set; }

        [ListProperty]
        [DisplayName("字段名称映射")]
        [Description("配置输入数据中的字段名称映射。如果未配置，将尝试匹配默认名称（如“工单号”、“加工时长”等）。")]
        public List<FieldMappingItem> FieldMappings { get; set; } = new List<FieldMappingItem>();

        [DisplayName("排序规则")]
        [Description("选择用于排序的 APS 算法。")]
        public SchedulingRule Rule { get; set; } = SchedulingRule.EDD;

        [ResultToProperty]
        [DisplayName("将结果保存到变量")]
        [Description("排序后的工单集合将存储在此变量中。")]
        public string ResultTo { get; set; } = "SortedWorkOrders";

        public IEnumerable<GenerateParam> GetGenerateParams()
        {
            yield return new GenerateListParam()
            {
                ParamName = this.ResultTo,
                Description = "排序后的工单集合",
                ItemProperties = new List<string> 
                { 
                    nameof(WorkOrder.OrderID), 
                    nameof(WorkOrder.ProcessingTime), 
                    nameof(WorkOrder.DueDate), 
                    nameof(WorkOrder.Priority), 
                    nameof(WorkOrder.ArrivalTime),
                    nameof(WorkOrder.SequenceNo)
                },
                ItemPropertiesDescription = new Dictionary<string, string>
                {
                    { nameof(WorkOrder.OrderID), "工单号" },
                    { nameof(WorkOrder.ProcessingTime), "加工时长" },
                    { nameof(WorkOrder.DueDate), "交货日期" },
                    { nameof(WorkOrder.Priority), "订单优先级" },
                    { nameof(WorkOrder.ArrivalTime), "到达时间" },
                    { nameof(WorkOrder.SequenceNo), "排程序号" }
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

                // 2. 准备字段映射字典
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

                // 3. 提取数据
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
                    // 1. 获取外部名称和内部 key
                    string extOrderID = mappingDict[InternalField.OrderID];
                    string extProcessingTime = mappingDict[InternalField.ProcessingTime];
                    string extDueDate = mappingDict[InternalField.DueDate];
                    string extPriority = mappingDict[InternalField.Priority];
                    string extArrivalTime = mappingDict[InternalField.ArrivalTime];

                    // 2. 解析数据用于排序
                    order.OrderID = jo[extOrderID]?.ToString() ?? "N/A";
                    order.ProcessingTime = ParseDouble(jo[extProcessingTime], 0);
                    order.DueDate = ParseDateTime(jo[extDueDate], DateTime.MaxValue);
                    order.Priority = ParseDouble(jo[extPriority], 1.0);
                    order.ArrivalTime = ParseDateTime(jo[extArrivalTime], DateTime.Now);

                    // 3. 标准化 ResultTo 中的 key (将用户映射的字段名改回内部标准名，如 "ID", "ProcessingTime" 等)
                    // 如果外部字段名和内部字段名不一致，则进行转换
                    UpdateResultField(resultJo, extOrderID, nameof(WorkOrder.OrderID));
                    UpdateResultField(resultJo, extProcessingTime, nameof(WorkOrder.ProcessingTime));
                    UpdateResultField(resultJo, extDueDate, nameof(WorkOrder.DueDate));
                    UpdateResultField(resultJo, extPriority, nameof(WorkOrder.Priority));
                    UpdateResultField(resultJo, extArrivalTime, nameof(WorkOrder.ArrivalTime));

                    orderPairs.Add((order, resultJo));
                }

                // 4. 执行规则校验
                string validationError = ValidateData(orderPairs.Select(p => p.Order).ToList(), Rule);
                if (!string.IsNullOrEmpty(validationError))
                {
                    return new ExecuteResult { Message = validationError };
                }

                // 5. 执行排序逻辑
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
                        // 先来先服务，按到达时间排序
                        sortedPairs = orderPairs.OrderBy(p => p.Order.ArrivalTime);
                        break;
                    default:
                        sortedPairs = orderPairs;
                        break;
                }

                // 6. 返回结果（返回标准化 key 后的 JObject，同时保留了其他非映射字段）
                var resultList = new List<JObject>();
                int sequence = 1;
                foreach (var pair in sortedPairs)
                {
                    var jo = pair.Result;
                    jo[nameof(WorkOrder.SequenceNo)] = sequence++;
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