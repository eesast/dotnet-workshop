using LogAnalyzer;
using LogAnalyzerAgent.Applications;
using LogAnalyzerAgent.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;              // for Desktop
        // listenOptions.Protocols = HttpProtocols.Http1AndHttp2;   // for Browser
    });
});

builder.Services.AddGrpc();
builder.Services.AddCors();

// 依赖注入，且有状态服务需要单例
builder.Services.AddSingleton<LogFileAnalyzer>();
builder.Services.AddSingleton<AgentSession>();
builder.Services.AddSingleton<AgentService>();

var app = builder.Build();

var whiteList = new HashSet<string>()
{
    "http://localhost:5235",
    "https://localhost:7169",
    "http://localhost:57814",
    "https://localhost:57815",
    "http://127.0.0.1:5235",
    "https://127.0.0.1:7169",
    "http://127.0.0.1:57814",
    "https://127.0.0.1:57815",
};
app.UseCors(policy =>
{
    policy
        .SetIsOriginAllowed(origin =>
            string.IsNullOrEmpty(origin) || whiteList.Contains(origin))
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
});

// for Browser
app.UseGrpcWeb();

// for Browser
app.MapGrpcService<AgentService>()
    .EnableGrpcWeb();

app.Run();
