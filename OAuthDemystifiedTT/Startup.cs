using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.AzureAD.UI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using OauthHandler;
using OpenIdConnect.AzureAdSample;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;

namespace OAuthDemystifiedTT
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy());
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddAuthentication(sharedOptions =>
            {
                sharedOptions.DefaultChallengeScheme = "TechtalkScheme";
                sharedOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
                .AddCookie(options =>
                {                    
                    options.Events.OnRedirectToLogin = context =>
                    {
                        throw new Exception("Not logged in!");
                    };
                })
                .AddRemoteScheme<OauthConnectOptions, OauthConnectHandler>("TechtalkScheme", "Master demystifier", options =>
                {
                    Configuration.Bind("AzureAd", options);
                    options.Authority = "";
                    options.MetadataAddress = "";
                    string stsDiscoveryEndpoint = "https://login.microsoftonline.com/ibmke.onmicrosoft.com/v2.0/.well-known/openid-configuration";

                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(stsDiscoveryEndpoint, new OpenIdConnectConfigurationRetriever());
                    options.Configuration = options.ConfigurationManager.GetConfigurationAsync(CancellationToken.None).Result;
                });

            var storageAccount = CloudStorageAccount.Parse($"DefaultEndpointsProtocol=https;AccountName={Environment.GetEnvironmentVariable("STORAGE_NAME")};AccountKey={Environment.GetEnvironmentVariable("STORAGE_SECRET")};EndpointSuffix=core.windows.net");
            var client = storageAccount.CreateCloudBlobClient();
            var container = client.GetContainerReference("key-container");

            container.CreateIfNotExistsAsync().GetAwaiter().GetResult();

            services.AddDataProtection()
                .SetApplicationName("OAuth demystified")
                .PersistKeysToAzureBlobStorage(container, "keys.xml");

            services.AddMvc(options =>
            {
                options.EnableEndpointRouting = false;
            })
                .AddRazorPagesOptions(options =>
                {
                    options.Conventions.AuthorizePage("/Authgrant");
                    options.Conventions.AllowAnonymousToPage("/Index");
                    options.Conventions.AllowAnonymousToPage("/Client");
                    options.Conventions.AllowAnonymousToPage("/Implicit");
                });

            services.AddSingleton<IAuthorizationHandler, ValidAADTokenHandler>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHealthChecks("/liveness", new HealthCheckOptions
            {
                Predicate = r => r.Name.Contains("self")
            });

            app.UseHealthChecks("/hc", new HealthCheckOptions()
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            app.UseHealthChecksUI();

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseAuthentication();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                   name: "default",
                   template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
