using LogAnalyzerAgent.Applications;
using LogAnalyzerAgent.Auth;
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

// 依赖注入，且有状态服务需要单例。
// T5.1.a.b：以 TokenStore（鉴权）+ SessionManager（按 token 隔离的 Analyzer）替代原先的单个共享 Analyzer。
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<AgentSession>();
builder.Services.AddSingleton<AgentService>();

var app = builder.Build();

// 启动时生成一个管理员 token，并以 log 形式输出，供使用者首次登录客户端时取用（T5.1.a.b）。
var tokenStore = app.Services.GetRequiredService<TokenStore>();
var bootstrapLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
string adminToken = tokenStore.CreateAdminToken();
bootstrapLogger.LogInformation(
    "Admin token generated. Use it to log in the client (File -> Connect...) and manage other tokens: {Token}",
    adminToken);

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
