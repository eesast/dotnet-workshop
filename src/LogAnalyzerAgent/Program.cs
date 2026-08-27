using LogAnalyzer;
using LogAnalyzerAgent.Applications;
using LogAnalyzerAgent.Infrastructure;
using LogAnalyzerAgent.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// 仅使用控制台日志，确保初始 Admin Token 一定打印在控制台，并避免 EventLog 写入权限问题。
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2; // 兼顾 Desktop & Web
    });
});

// 1. 注册多用户 Token 管理器与 Session 隔离管理器
builder.Services.AddSingleton<TokenManager>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<AuthInterceptor>();

// 2. 注册 gRPC 服务并启用 Auth 拦截器
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<AuthInterceptor>();
});

builder.Services.AddCors();
builder.Services.AddSingleton<AgentService>();

var app = builder.Build();

// 3. 显式触发 TokenManager 初始化，自动生成并打印初始 Admin Token
app.Services.GetRequiredService<TokenManager>();

// 跨域白名单配置
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
        .SetIsOriginAllowed(origin => string.IsNullOrEmpty(origin) || whiteList.Contains(origin))
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
});

app.UseGrpcWeb();
app.MapGrpcService<AgentService>().EnableGrpcWeb();

app.Run();