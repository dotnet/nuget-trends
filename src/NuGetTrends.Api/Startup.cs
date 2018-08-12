using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Swagger;

namespace NuGetTrends.Api
{
    public class Startup
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly ILoggerFactory _loggerFactory;
        public IConfiguration Configuration { get; }

        public Startup(
            IConfiguration configuration,
            IHostingEnvironment hostingEnvironment,
            ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            Configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc()
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1)
                .AddJsonOptions(o => o.SerializerSettings.NullValueHandling = NullValueHandling.Ignore);

            if (_hostingEnvironment.IsDevelopment())
            {
                services.AddCors(options =>
                {
                    options.AddPolicy("AllowAll",
                        builder =>
                        {
                            builder
                                .AllowAnyOrigin()
                                .AllowAnyMethod()
                                .AllowAnyHeader()
                                .AllowCredentials()
                                .SetPreflightMaxAge(TimeSpan.FromDays(1));
                            ;
                        });
                });
            }

            services
                .AddEntityFrameworkSqlServer()
                .AddDbContext<NuGetMustHavesContext>(options =>
                {
                    options.UseSqlServer(Configuration.GetConnectionString("NuGetMustHaves"));
                    if (_hostingEnvironment.IsDevelopment())
                    {
                        var logger = _loggerFactory.CreateLogger<Startup>();
                        logger.LogWarning("Enabling EF Core " + nameof(options.EnableSensitiveDataLogging));
                        options.EnableSensitiveDataLogging();
                    }
                });

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Info { Title = "NuGet Trends", Version = "v1" });
                var xmlFile = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });
        }

        public void Configure(IApplicationBuilder app)
        {
            if (_hostingEnvironment.IsDevelopment())
            {
                app.UseCors("AllowAll");
                app.UseMiddleware<ExceptionInResponseMiddleware>();
            }
            else
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sentry Ornigram");

                c.DocumentTitle = "NuGet Trends API";
                c.DocExpansion(DocExpansion.None);

            });

            app.UseMvc();
        }
    }
}
