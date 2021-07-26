using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NuGetTrends.Data;
using Swashbuckle.AspNetCore.SwaggerUI;
using Microsoft.OpenApi.Models;
using Sentry.AspNetCore;
using Sentry.Tunnel;
using Shortr;
using Shortr.Npgsql;

namespace NuGetTrends.Web
{
    public class Startup
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration, IWebHostEnvironment hostingEnvironment)
        {
            _configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSentryTunneling(); // Add Sentry Tunneling to avoid ad-blockers.
            services.AddControllers();
            services.AddHttpClient();

            // In production, the Angular files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "Portal/dist";
            });

            if (_hostingEnvironment.IsDevelopment())
            {
                // keep cors during development so we can still run the spa on Angular default port (4200)
                services.AddCors(options =>
                {
                    options.AddPolicy("AllowAll",
                        builder =>
                        {
                            builder
                                .AllowAnyOrigin()
                                .AllowAnyMethod()
                                .AllowAnyHeader()
                                .SetPreflightMaxAge(TimeSpan.FromDays(1));
                        });
                });
            }

            services.AddDbContext<NuGetTrendsContext>(options =>
            {
                options.UseNpgsql(_configuration.GetConnectionString("NuGetTrends"));
                if (_hostingEnvironment.IsDevelopment())
                {
                    options.EnableSensitiveDataLogging();
                }
            });

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo {Title = "NuGet Trends", Version = "v1"});
                var xmlFile = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            services.AddShortr();
            if (!_hostingEnvironment.IsDevelopment())
            {
                services.Replace(ServiceDescriptor.Singleton<IShortrStore, NpgsqlShortrStore>());
                services.AddSingleton(c => new NpgsqlShortrOptions
                {
                    ConnectionString = c.GetRequiredService<IConfiguration>().GetConnectionString("NuGetTrends")
                });
            }
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseStaticFiles();
            if (!_hostingEnvironment.IsDevelopment())
            {
                app.UseSpaStaticFiles();
            }

            app.UseRouting();
            app.UseSentryTracing();

            // Proxy Sentry events from the frontend to sentry.io
            app.UseSentryTunneling("/t");

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
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "Portal";
                if (_hostingEnvironment.IsDevelopment())
                {
                    // use the external angular CLI server instead
                    spa.UseProxyToSpaDevelopmentServer("http://localhost:4200");
                }
            });

            app.UseShortr();
        }
    }
}
