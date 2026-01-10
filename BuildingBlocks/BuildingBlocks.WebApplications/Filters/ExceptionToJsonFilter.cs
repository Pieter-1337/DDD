using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildingBlocks.WebApplications.Filters
{
    public class ExceptionToJsonFilter : IExceptionFilter, IActionFilter
    {
        private readonly ILogger<ExceptionToJsonFilter> _logger;
        private const string ActionArgumentsKey = "ActionArgumentsJson";

        public ExceptionToJsonFilter(ILogger<ExceptionToJsonFilter> logger)
        {
            _logger = logger;
        }

        // IActionFilter - capture arguments before action executes
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.ActionArguments.Count == 0)
                return;

            // Store serialized arguments for potential error logging
            context.HttpContext.Items[ActionArgumentsKey] =
                System.Text.Json.JsonSerializer.Serialize(context.ActionArguments);
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        // IExceptionFilter - handle exceptions
        public void OnException(ExceptionContext context)
        {
            if (context.ExceptionHandled)
                return;

            switch (context.Exception)
            {
                case ValidationException validationEx:
                    HandleValidationException(context, validationEx);
                    break;
                default:
                    HandleUnexpectedException(context);
                    break;
            }

            context.ExceptionHandled = true;
        }

        private void HandleValidationException(ExceptionContext context, ValidationException exception)
        {
            var response = new ValidationErrorWrapper(exception);

            context.Result = new JsonResult(response)
            {
                StatusCode = response.HttpStatusCode
            };
        }

        private void HandleUnexpectedException(ExceptionContext context)
        {
            // Log with request details including the captured arguments
            _logger.LogError(context.Exception,
                "Unhandled exception in {Method} {Path} - Arguments: {Arguments}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.GetDisplayUrl(),
                context.HttpContext.Items[ActionArgumentsKey]);

            context.Result = new JsonResult(new
            {
                Title = "An unexpected error occurred",
                Status = StatusCodes.Status500InternalServerError
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
