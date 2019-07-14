using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NuGetTrends.Data;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace NuGetTrends.Api
{
    public class Startup
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        public IConfiguration Configuration { get; }

        public Startup(
            IConfiguration configuration,
            IHostingEnvironment hostingEnvironment)
        {
            Configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc()
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

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
                .AddEntityFrameworkNpgsql()
                .AddDbContext<NuGetTrendsContext>(options =>
                {
                    options.UseNpgsql(Configuration.GetConnectionString("NuGetTrends"));
                    if (_hostingEnvironment.IsDevelopment())
                    {
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

            services.AddSpaStaticFiles(p => p.RootPath = "wwwroot");
        }

        public void Configure(IApplicationBuilder app)
        {
            if (_hostingEnvironment.IsDevelopment())
            {
                app.UseCors("AllowAll");
                app.UseMiddleware<ExceptionInResponseMiddleware>();

                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "NuGet Trends");

                    c.DocumentTitle = "NuGet Trends API";
                    c.DocExpansion(DocExpansion.None);
                });
            }

            app.UseSwagger();
            app.UseMvc();
        }
    }
}
