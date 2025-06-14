using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using Cryptic_Domain.Interfaces.Access;
using Cryptic.Base.V1.Models.Responses;
using Cryptic_Domain.ResponseFactory;
using Cryptic_Domain.Interfaces.Access;

namespace Cryptic_Domain.Interceptors
{
    public class LoggingInterceptor : Interceptor
    {
        public LoggingInterceptor(ILogger<LoggingInterceptor> logger, IRequestIdAccessor requestIdAccessor,
            IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _requestIdAccessor = requestIdAccessor;
            _httpContextAccessor = httpContextAccessor;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            string methodName = context.Method;

            string requestId = context.RequestHeaders.FirstOrDefault(h => h.Key == REQUEST_ID_HEADER_NAME)?.Value;

            if (string.IsNullOrEmpty(requestId))
            {
                _logger.LogError(
                    "[XRequestId] Request ID is missing. Cannot process the request for method {MethodName}.",
                    methodName);

                throw new RpcException(new Status(StatusCode.InvalidArgument, "Request ID is missing."));
            }

            _requestIdAccessor.RequestId = requestId;
            _httpContextAccessor.HttpContext?.Items.TryAdd(REQUEST_ID_HEADER_NAME, requestId);

            object sanitizedRequest = SanitizeObject(request);

            using (_logger.BeginScope(new { RequestId = requestId }))
            {
                _logger.LogInformation(
                    "[XRequestId] Received request with id {RequestId} for {MethodName} with parameters: {@Request}",
                    requestId, methodName, sanitizedRequest);

                try
                {
                    TResponse response = await continuation(request, context);
                    object sanitizedResponse = SanitizeObject(response);
                    _logger.LogInformation("[XRequestId] Response for {MethodName}: {@Response}", methodName, sanitizedResponse);
                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[XRequestId] {RequestId} Unknown error in method {MethodName}: {Message}", requestId, methodName, ex.Message);
                    return CreateErrorResponse<TResponse>(ex.Message, false);
                }
                finally
                {
                    _requestIdAccessor.RequestId = null;
                }
            }
        }

        private TResponse CreateErrorResponse<TResponse>(string errorMessage, bool isUserError) where TResponse : class
        {
            TResponse response = Activator.CreateInstance<TResponse>();

            Type responseType = typeof(TResponse);

            if (responseType == typeof(TaskResponse))
            {
                return TaskResponseFactory.CreateWithError(errorMessage, Guid.NewGuid().ToString(), isUserError)
                    as TResponse;
            }

            PropertyInfo? resultProperty = _propertyCache.GetOrAdd(responseType,
                type => type.GetProperty("Result", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
            string test = $"Expected Type: {typeof(TaskResponse).FullName} from {typeof(TaskResponse).Assembly.FullName}";
            string test2 = $"Actual Type: {resultProperty?.PropertyType.FullName} from {resultProperty?.PropertyType.Assembly.FullName}";

            if (resultProperty != null && resultProperty.PropertyType.FullName == typeof(TaskResponse).FullName)
            {
                Action<object, TaskResponse> setter = _setterCache.GetOrAdd(responseType, type =>
                {
                    System.Linq.Expressions.ParameterExpression parameterExpression = System.Linq.Expressions.Expression.Parameter(typeof(object), "obj");
                    System.Linq.Expressions.ParameterExpression valueExpression = System.Linq.Expressions.Expression.Parameter(typeof(TaskResponse), "value");

                    System.Linq.Expressions.UnaryExpression castObj = System.Linq.Expressions.Expression.Convert(parameterExpression, type);
                    System.Linq.Expressions.MethodCallExpression propertySetter = System.Linq.Expressions.Expression.Call(
                        castObj,
                        resultProperty.GetSetMethod(),
                        valueExpression
                    );

                    System.Linq.Expressions.Expression<Action<object, TaskResponse>> lambda = System.Linq.Expressions.Expression.Lambda<Action<object, TaskResponse>>(
                        propertySetter,
                        parameterExpression,
                        valueExpression
                    );

                    return lambda.Compile();
                });

                TaskResponse taskResponse =
                    TaskResponseFactory.CreateWithError(errorMessage, Guid.NewGuid().ToString(), isUserError);
                setter(response, taskResponse);
            }

            return response;
        }

        private object SanitizeObject<T>(T obj)
        {
            if (obj == null) return null;

            PropertyInfo[] properties = typeof(T).GetProperties();

            // Create a sanitized version of the object
            Dictionary<string, object> sanitizedObject = new();

            foreach (PropertyInfo prop in properties)
            {
                object? value = prop.GetValue(obj);

                // Check for byte[] or ByteString and mask them
                if (value is byte[] or Google.Protobuf.ByteString)
                {
                    sanitizedObject[prop.Name] = "*** binary data omitted ***";
                }
                else if (!prop.PropertyType.FullName.Contains("Google.Protobuf") || prop.PropertyType.FullName.Contains("Google.Protobuf.Collections.RepeatedField"))
                {
                    sanitizedObject[prop.Name] = value;
                }
            }

            return sanitizedObject;
        }

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<LoggingInterceptor> _logger;
        private readonly IRequestIdAccessor _requestIdAccessor;
        private const string REQUEST_ID_HEADER_NAME = "x-request-id";

        private static readonly ConcurrentDictionary<Type, Action<object, TaskResponse>> _setterCache =
            new();

        private static readonly ConcurrentDictionary<Type, PropertyInfo> _propertyCache =
            new();
    }
}