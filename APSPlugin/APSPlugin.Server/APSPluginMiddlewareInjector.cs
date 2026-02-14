using System;
using System.Collections.Generic;
using GrapeCity.Forguncy.ServerApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace APSPlugin.Server
{
    public class APSPluginMiddlewareInjector : MiddlewareInjector
    {
        public override List<ServiceItem> ConfigureServices(List<ServiceItem> serviceItems, IServiceCollection services)
        {
            serviceItems.Insert(0, new ServiceItem()
            {
                Id = "a1674470-235e-4295-8903-61c308d166b6",
                ConfigureServiceAction = () =>
                {
                    // 这里可以注册中间件需要的服务，相当于 Asp.net 中的 public void ConfigureServices(IServiceCollection services) 方法
                    //services.AddXXXService();
                },
                Description = "我的自定义中间件服务"
            });
            return base.ConfigureServices(serviceItems, services);
        }
        public override List<MiddlewareItem> Configure(List<MiddlewareItem> middlewareItems, IApplicationBuilder app)
        {
            middlewareItems.Insert(0, new MiddlewareItem()
            {
                Id = "a1674470-235e-4295-8903-61c308d166b6",
                ConfigureMiddleWareAction = () =>
                {
                    app.UseMiddleware<APSPluginMiddleware>();
                },
                Description = "我的自定义中间件"
            });
            return base.Configure(middlewareItems, app);
        }
    }
}
