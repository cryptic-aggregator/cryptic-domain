using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Cryptic_Domain.Interfaces.Access;

namespace Trident_PWA_Domain.Interceptors;

public class ClientRequestIdInterceptor : Interceptor
{
    public ClientRequestIdInterceptor(IHttpContextAccessor httpContextAccessor,
        ILogger<ClientRequestIdInterceptor> logger, IRequestIdAccessor requestIdAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _requestIdAccessor = requestIdAccessor;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        string requestId = null;

        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && httpContext.Items.TryGetValue(RequestIdHeader, out object? requestIdObj))
        {
            requestId = requestIdObj as string;
            _logger.LogInformation("[XRequestId] Retrieved Request ID from HttpContext: {RequestId}", requestId);
        }

        if (string.IsNullOrEmpty(requestId))
        {
            requestId = _requestIdAccessor.RequestId;
            _logger.LogInformation("[XRequestId] Retrieved Request ID from RequestIdAccessor: {RequestId}", requestId);
        }

        if (string.IsNullOrEmpty(requestId))
        {
            requestId = Guid.NewGuid().ToString();
            _requestIdAccessor.RequestId = requestId;
            _logger.LogInformation("[XRequestId] Generated new Request ID: {RequestId}", requestId);
        }

        Metadata headers = new()
        {
            { RequestIdHeader, requestId }
        };

        CallOptions newOptions = context.Options.WithHeaders(headers);
        ClientInterceptorContext<TRequest, TResponse> newContext = new(
            context.Method,
            context.Host,
            newOptions);

        _logger.LogInformation("[XRequestId] Adding Request ID: {RequestId} to outgoing gRPC call to {Method}",
            requestId, context.Method.FullName);

        AsyncUnaryCall<TResponse> call = continuation(request, newContext);

        return new AsyncUnaryCall<TResponse>(
            call.ResponseAsync,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRequestIdAccessor _requestIdAccessor;
    private readonly ILogger<ClientRequestIdInterceptor> _logger;
    private const string RequestIdHeader = "x-request-id";
}