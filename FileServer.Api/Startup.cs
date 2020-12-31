using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using AutoMapper;
using FileServer.Api.Configuration.AutoMappers;
using FileServer.Api.Configuration.Consul;
using FileServer.Api.Configuration.Swagger;
using FileServer.Api.Filters;
using FileServer.Api.Middleware;
using FileServer.Api.RequestBody;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;


namespace FileServer.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        /// <summary>
        /// �����б�
        /// </summary>
        private static readonly List<string> _Assemblies = new List<string>()
        {
            "Peihui.Common.Security",
            "FileServer.Common",
            "FileServer.ApplicationServer",
            "FileServer.DomainServer"
        };
        /// <summary>
        /// ����ע��
        /// </summary>
        /// <param name="container"></param>
        public void ConfigureContainer(ContainerBuilder container)
        {
            var assemblys = _Assemblies.Select(x => Assembly.Load(x)).ToList();
            List<Type> allTypes = new List<Type>();
            assemblys.ForEach(aAssembly =>
            {
                allTypes.AddRange(aAssembly.GetTypes());
            });

            // ͨ��Autofac�Զ��������ע��
            container.RegisterTypes(allTypes.ToArray())
                .AsImplementedInterfaces()
                .PropertiesAutowired()
                .InstancePerDependency();

            // ע��Controller
            container.RegisterAssemblyTypes(typeof(Startup).GetTypeInfo().Assembly)
                .Where(t => typeof(Controller).IsAssignableFrom(t) && t.Name.EndsWith("Controller", StringComparison.Ordinal))
                .PropertiesAutowired();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            #region ȫ���쳣����
            //services.AddMvc(options =>
            //{
            //    options.Filters.Add<HttpGlobalExceptionFilter>();
            //});
            #endregion

            services.AddControllers(o => o.InputFormatters.Insert(0, new RawRequestBodyFormatter()))
               .AddNewtonsoftJson(options =>
               {
                   //�޸��������Ƶ����л���ʽ������ĸСд
                   options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

                   //�޸�ʱ������л���ʽ
                   //options.SerializerSettings.Converters.Add(new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss" });
                   //options.SerializerSettings.DateFormatHandling = Newtonsoft.Json.DateFormatHandling.IsoDateFormat;
                   options.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss";
               });
            //ע��http�������
            services.AddHttpClient();

            #region Swagger������
            services.AddSwaggerGen(c =>
            {
                // => ����v1
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "����MinIO�ļ���������̨API",
                    Version = "v1",
                    Contact = new OpenApiContact
                    {
                        Name = "�����",
                        Email = "2019070053@sanhepile.com",
                        Url = new Uri("https://github.com/peihuiyang")
                    }
                });
                
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "Token��¼��֤,��ʽ��Bearer {token}(ע������֮����һ���ո�)",
                    Name = "Authorization",
                    //���������������޸�
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement {
                     {
                          new OpenApiSecurityScheme
                          {
                                Reference = new OpenApiReference
                                {
                                      Type = ReferenceType.SecurityScheme,
                                      Id = "Bearer"
                                }
                          },
                          new string[] { }
                     }
                });
                var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);//��ȡӦ�ó�������Ŀ¼�����ԣ����ܹ���Ŀ¼Ӱ�죬������ô˷�����ȡ·����
                //var basePath = PlatformServices.Default.Application.ApplicationBasePath;
                var xmlPath = Path.Combine(basePath, "FileServer.Api.xml");
                c.IncludeXmlComments(xmlPath);//�ڶ�������true��ʾ�ÿ�������XMLע�͡�Ĭ��false
                var entityDtoXmlPath = Path.Combine(basePath, "FileServer.EntityDto.xml");
                c.IncludeXmlComments(entityDtoXmlPath);
                //��ӶԿ������ı�ǩ(����)
                c.DocumentFilter<SwaggerDocTag>();
                //c.OperationFilter<SwaggerFileUploadFilter>();
            });
            #endregion

            #region ʹ��AutoMapper
            services.AddAutoMapper(typeof(MapperProfiles));
            #endregion

            #region �����ϴ��ļ���С����
            services.Configure<KestrelServerOptions>(options =>
            {
                // Set the limit to 256 MB
                options.Limits.MaxRequestBodySize = 268435456;
            });
            #endregion
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            #region ע�����ConsulServer
            this.Configuration.ConsulRegister();
            #endregion

            #region ȫ���쳣����
            app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
            #endregion

            #region Swagger�м������
            //�����м����������Swagger��ΪJSON�ս��
            app.UseSwagger();
            //�����м�������swagger-ui��ָ��Swagger JSON�ս��
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "���͹�׮�ļ���������̨API V1");
                c.RoutePrefix = string.Empty;
            });
            #endregion           

            //����֧��nginx
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
