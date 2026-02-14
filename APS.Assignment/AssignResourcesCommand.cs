using GrapeCity.Forguncy.Commands;
using GrapeCity.Forguncy.Plugin;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace APS.Assignment
{
    [Icon("Resources/Icon.png")]
    [Category("APS 高级排程")]
    [OrderWeight(100)]
    public class AssignResourcesCommand : Command, ICommandExecutableInServerSideAsync
    {
        [FormulaProperty]
        [DisplayName("排产工单列表 (List<Object>)")]
        [Description("包含 Id(工单号) 和 Duration(时长/分钟) 的对象列表。通常由排序插件输出。")]
        public object SortedOrders { get; set; }

        [FormulaProperty]
        [DisplayName("可用资源列表 (List<Object>)")]
        [Description("包含 ResourceId(资源号) 和 Efficiency(效率系数, 默认1) 的对象列表。")]
        public object Resources { get; set; }

        [FormulaProperty]
        [DisplayName("全局计划开始时间 (DateTime)")]
        [Description("排程的起始时间点。如果不填，默认为当前服务器时间。")]
        public object GlobalStartTime { get; set; }

        [ResultToProperty]
        [DisplayName("将排程结果保存到变量")]
        [Description("输出包含 TaskId, ResourceId, StartTime, EndTime 的列表。")]
        public string Result { get; set; }

        public override string ToString()
        {
            return "APS 资源分配 (EFT算法)";
        }

        public async Task<ExecuteResult> ExecuteAsync(IServerCommandExecuteContext context)
        {
            var result = new ExecuteResult();

            try
            {
                // 1. 解析参数
                var orders = await GetOrdersAsync(context);
                var resources = await GetResourcesAsync(context);
                
                var startTimeObj = await context.EvaluateFormulaAsync(GlobalStartTime);
                var globalStart = DateTime.Now;
                if (startTimeObj != null)
                {
                    if (startTimeObj is DateTime dt) globalStart = dt;
                    else if (DateTime.TryParse(startTimeObj.ToString(), out var parsedDt)) globalStart = parsedDt;
                }

                // 2. 初始化资源可用性表
                var resourceAvailability = new Dictionary<string, DateTime>();
                foreach (var res in resources)
                {
                    var resStart = res.InitialAvailableTime ?? globalStart;
                    if (resStart < globalStart) resStart = globalStart;
                    resourceAvailability[res.Id] = resStart;
                }

                var assignmentResults = new List<AssignmentOutput>();

                // 3. 核心算法：EFT (Earliest Finish Time)
                foreach (var order in orders)
                {
                    string bestResource = null;
                    DateTime bestFinishTime = DateTime.MaxValue;
                    DateTime bestStartTime = DateTime.MaxValue;

                    foreach (var res in resources)
                    {
                        if (!resourceAvailability.ContainsKey(res.Id)) continue;

                        var availTime = resourceAvailability[res.Id];
                        var start = availTime; // 已经保证 >= globalStart

                        var eff = res.Efficiency > 0 ? res.Efficiency : 1.0;
                        var actualDurationMinutes = order.Duration / eff;

                        var finish = start.AddMinutes(actualDurationMinutes);

                        if (finish < bestFinishTime)
                        {
                            bestFinishTime = finish;
                            bestStartTime = start;
                            bestResource = res.Id;
                        }
                    }

                    if (bestResource != null)
                    {
                        assignmentResults.Add(new AssignmentOutput
                        {
                            TaskId = order.Id,
                            ResourceId = bestResource,
                            StartTime = bestStartTime,
                            EndTime = bestFinishTime
                        });

                        resourceAvailability[bestResource] = bestFinishTime;
                    }
                }

                // 4. 返回结果
                if (!string.IsNullOrEmpty(Result))
                {
                    // 使用 JArray/JObject 构造返回结果，这样活字格可以自动识别结构
                    var jArray = JArray.FromObject(assignmentResults);
                    context.Parameters[Result] = jArray;
                }
            }
            catch (Exception ex)
            {
                return new ExecuteResult 
                { 
                    Message = $"APS Resource Assignment Error: {ex.Message}" 
                };
            }

            return result;
        }

        // --- Helpers ---

        private async Task<List<OrderInput>> GetOrdersAsync(IServerCommandExecuteContext context)
        {
            var list = new List<OrderInput>();
            var raw = await context.EvaluateFormulaAsync(SortedOrders);
            if (raw == null) return list;

            // 统一转成 JArray 处理
            var json = JsonConvert.SerializeObject(raw);
            var jArray = JsonConvert.DeserializeObject<JArray>(json);

            if (jArray != null)
            {
                foreach (var item in jArray)
                {
                    list.Add(new OrderInput
                    {
                        Id = GetJValue(item, "Id", "WorkOrder", "TaskId"),
                        Duration = GetJValue<double>(item, 0, "Duration", "Time", "ProcessingTime")
                    });
                }
            }
            return list;
        }

        private async Task<List<ResourceInput>> GetResourcesAsync(IServerCommandExecuteContext context)
        {
            var list = new List<ResourceInput>();
            var raw = await context.EvaluateFormulaAsync(Resources);
            if (raw == null) return list;

            var json = JsonConvert.SerializeObject(raw);
            var jArray = JsonConvert.DeserializeObject<JArray>(json);

            if (jArray != null)
            {
                foreach (var item in jArray)
                {
                    list.Add(new ResourceInput
                    {
                        Id = GetJValue(item, "ResourceId", "Name", "Id"),
                        Efficiency = GetJValue<double>(item, 1.0, "Efficiency", "Factor"),
                        InitialAvailableTime = GetJValue<DateTime?>(item, null, "InitialAvailableTime")
                    });
                }
            }
            return list;
        }

        private string GetJValue(JToken item, params string[] keys)
        {
            foreach (var key in keys)
            {
                // JObject 键查找（忽略大小写）
                var val = item[key]; // 严格匹配
                if (val == null && item is JObject jo)
                {
                    // 尝试忽略大小写查找
                    var prop = jo.Properties().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (prop != null) val = prop.Value;
                }

                if (val != null) return val.ToString();
            }
            return null;
        }

        private T GetJValue<T>(JToken item, T defaultValue, params string[] keys)
        {
            foreach (var key in keys)
            {
                var val = item[key];
                if (val == null && item is JObject jo)
                {
                    var prop = jo.Properties().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (prop != null) val = prop.Value;
                }

                if (val != null)
                {
                    try
                    {
                        return val.ToObject<T>();
                    }
                    catch { }
                }
            }
            return defaultValue;
        }

        // --- Data Structures ---

        private class OrderInput
        {
            public string Id { get; set; }
            public double Duration { get; set; } // Minutes
        }

        private class ResourceInput
        {
            public string Id { get; set; }
            public double Efficiency { get; set; } = 1.0;
            public DateTime? InitialAvailableTime { get; set; }
        }

        private class AssignmentOutput
        {
            public string TaskId { get; set; }
            public string ResourceId { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }
    }
}
