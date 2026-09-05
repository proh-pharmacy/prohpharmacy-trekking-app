using FluentValidation.Results;

namespace prohpharmacy_trekking_app.Shared
{
    public class Error
    {
        public string Code { get; private set; }
        public string Message { get; private set; }

        public Error(string code, object message)
        {
            if (message is ValidationResult validationResult)
            {
                var errorDictionary = new Dictionary<string, string>();
                foreach (var error in validationResult.Errors)
                {
                    errorDictionary[error.PropertyName] = error.ErrorMessage;
                }
                Message = errorDictionary.First().Value;
                Code = "422";
            }
            else
            {
                Message = message.ToString()!;
                Code = code;
            }
        }

        public static Error BadRequest(string message)
            => new Error(StatusCodes.Status400BadRequest.ToString(), message);

        public static Error ValidationError(ValidationResult message)
            => new Error(StatusCodes.Status422UnprocessableEntity.ToString(), message);

        public static readonly Error None = new Error(string.Empty, string.Empty);

        public static readonly Error NullValue = new Error("Error.NullValue", "The specified result value is null.");

        public static readonly Error NotFound = new Error("404", "A Requested Item Was Not Found");

        public static Error CreateNotFoundError(string errorMessage)
            => new Error("404", errorMessage);

        public static Error Forbidden(string message = "You do not have permission to perform this action.")
            => new Error(StatusCodes.Status403Forbidden.ToString(), message);

        public static readonly Error InvalidRequest = new Error("Invalid Request", "Invalid Body Type");

        public static readonly Error ConditionNotMet = new Error("Error.ConditionNotMet", "The specified condition was not met.");

        public static Error Conflict(string message)
            => new Error(StatusCodes.Status409Conflict.ToString(), message);

        public static Error ServerError(string message = "An unexpected error occurred.")
            => new Error(StatusCodes.Status500InternalServerError.ToString(), message);
    }
}
