using Cryptic.Base.V1.Models.Responses;

namespace Cryptic_Domain.ResponseFactory
{
    public class TaskResponseFactory
    {
        public static TaskResponse CreateSuccessful()
        {
            return new TaskResponse { Success = true };
        }

        public static TaskResponse CreateWithError(string message, string errorTraceId, bool isExceptionForUser)
        {
            return new TaskResponse { Success = false, Error = CreateError(message, isExceptionForUser, errorTraceId) };
        }

        private static ErrorModel CreateError(string errorMessage, bool isExceptionForUser, string errorTraceId)
        {
            return new ErrorModel
            {
                ErrorTraceId = errorTraceId,
                IsExceptionForUser = isExceptionForUser,
                ErrorMessage = errorMessage
            };
        }
    }
}
