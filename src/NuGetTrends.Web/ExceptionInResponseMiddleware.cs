using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NuGetTrends.Web
{
    // ReSharper disable once ClassNeverInstantiated.Global - reflection
    internal class ExceptionInResponseMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionInResponseMiddleware> _logger;

        public ExceptionInResponseMiddleware(
            RequestDelegate next,
            ILogger<ExceptionInResponseMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                if (!context.Response.HasStarted)
                {
                    _logger.LogDebug("Caught exception: '{message}'. Sending as part of the response.", ex.Message);

                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "text/plain";

                    var responseBody = ex.ToString();
                    context.Response.ContentLength = Encoding.UTF8.GetByteCount(responseBody);
                    await context.Response.WriteAsync(responseBody);

                    if (!(context.Features.Get<IExceptionHandlerFeature>() is ExceptionHandlerFeature exceptionFeature))
                    {
                        exceptionFeature = new ExceptionHandlerFeature();
                        context.Features.Set<IExceptionHandlerFeature>(exceptionFeature);
                    }

                    exceptionFeature.Error = ex;
                }
                else
                {
                    _logger.LogWarning("Response has started. Cannot modify response header.");
                }
            }
        }
    }
}
