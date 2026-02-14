using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GrapeCity.Forguncy.Log;

namespace APSPlugin.Server
{
    internal class APSPluginMiddleware
    {
        private readonly RequestDelegate _next;
        public APSPluginMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.Value == "/APSPluginMiddleware")
            {
                context.Response.ContentType = "text/plain;charset=UTF-8";
                await context.Response.WriteAsync("自定义中间件测试成功");
                return;
            }
            await _next(context);
        }
    }
}
