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
using Shortr;
using Shortr.Npgsql;

namespace NuGetTrends.Api
{
    public class Startup
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        public IConfiguration Configuration { get; }

        public Startup(
            IConfiguration configuration,
            IWebHostEnvironment hostingEnvironment)
        {
            Configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
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
                                .SetPreflightMaxAge(TimeSpan.FromDays(1));
                        });
                });
            }

            services
                .AddDbContext<NuGetTrendsContext>(options =>
                {
                    options
                        .UseNpgsql(Configuration.GetConnectionString("NuGetTrends"));
                    if (_hostingEnvironment.IsDevelopment())
                    {
                        options.EnableSensitiveDataLogging();
                    }
                });

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "NuGet Trends", Version = "v1" });
                var xmlFile = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            services.AddShortr();
            services.Replace(ServiceDescriptor.Singleton<IShortrStore, NpgsqlShortrStore>());
            services.AddSingleton(c => new NpgsqlShortrOptions
            {
                ConnectionString = c.GetRequiredService<IConfiguration>().GetConnectionString("NuGetTrends")
            });
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
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
            app.UseEndpoints(endpoints => {
                endpoints.MapControllers();
            });
            app.UseShortr();
        }
    }
}
