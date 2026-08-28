using System;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace LogAnalyzerClient.Services
{
    /// <summary>
    /// 客户端 gRPC 拦截器（T5.1.a.b）：为每一次调用附加
    /// <c>authorization: Bearer &lt;token&gt;</c> 请求头，使 Agent 端能识别调用者身份。
    ///
    /// token 在创建客户端时（即 Connect 时）固定，整个客户端生命周期内不变。
    /// 通过覆盖 <see cref="Interceptor"/> 的全部调用入口，保证一元 / 流式调用都被附加头。
    /// </summary>
    public sealed class TokenInterceptor : Interceptor
    {
        private readonly string _token;

        public TokenInterceptor(string token)
        {
            _token = token ?? "";
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
            => continuation(request, WithToken(context));

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
            => continuation(request, WithToken(context));

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
            => continuation(request, WithToken(context));

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
            => continuation(WithToken(context));

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
            => continuation(WithToken(context));

        /// <summary>
        /// 返回一个附加了 token 头的新上下文。保留原有 metadata（去掉可能已有的 authorization 项以防重复），
        /// 并保留 deadline / cancellation 等其余选项。
        /// </summary>
        private ClientInterceptorContext<TRequest, TResponse> WithToken<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context)
            where TRequest : class
            where TResponse : class
        {
            var headers = new Metadata();
            if (context.Options.Headers is not null)
            {
                foreach (var entry in context.Options.Headers)
                {
                    if (!entry.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        headers.Add(entry);
                    }
                }
            }
            headers.Add("authorization", "Bearer " + _token);

            var newOptions = context.Options.WithHeaders(headers);
            return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
        }
    }
}
