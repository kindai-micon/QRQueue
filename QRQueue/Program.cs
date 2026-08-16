
using QRQueue.Handler;
using QRQueue.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QRQueue.Services;
using QRQueue.Repositories;
using QRQueue.Repositories.Implementations;
using System.Text.Json.Serialization;
using QRQueue.Hubs;
using Microsoft.AspNetCore.HttpOverrides;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using JsxCore;
using JsxCore.Hosting;
using JsxCore.Mvc;
namespace QRQueue
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Configure QuestPDF settings once at application startup
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.FontDiscoveryPaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "fonts"));

            using (var fontStream = File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "fonts", "NotoSansJP.ttf")))
            {
                FontManager.RegisterFont(fontStream);
            }

            var builder = WebApplication.CreateBuilder(args);

            // JsxCore: TSX/JSX ビューエンジン(Node.js 不要)
            builder.AddJsxCore(options =>
            {
                options.Document.Language = "ja";
            });

            // Add services to the container.

            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
            builder.Services.AddScoped<IPasscodeService, PasscodeService>();
            builder.Services.AddScoped<ITicketPdfGenerator, TicketPdfGenerator>();
            builder.Services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
            builder.Services.AddScoped<ITicketIssuanceService, TicketIssuanceService>();
            builder.Services.AddScoped<IGroupNumberIssuanceService, GroupNumberIssuanceService>();
            builder.Services.AddScoped<IQueueCallService, QueueCallService>();
            builder.Services.AddSingleton<IVapidService, VapidService>();
            builder.Services.AddSingleton<IPushSubscriptionService, PushSubscriptionService>();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSingleton<IAuthorityScanService, AuthorityScanService>();
            builder.Services.AddSwaggerGen();
            builder.Services.AddScoped<IAuthorizationHandler, DynamicRoleHandler>();
            builder.Services.AddSignalR();
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("lottery-db"));
            });
            // Repository層(コントローラ・サービスはこれ経由でデータアクセスする)
            builder.Services.AddScoped<IEventRepository, EventRepository>();
            builder.Services.AddScoped<IParticipationGroupRepository, ParticipationGroupRepository>();
            builder.Services.AddScoped<ITicketRepository, TicketRepository>();
            builder.Services.AddScoped<IIssueLogRepository, IssueLogRepository>();

            // CORS設定: 開発環境は全許可、本番はappsettings.jsonから取得
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    if (builder.Environment.IsDevelopment())
                    {
                        policy.SetIsOriginAllowed(_ => true)
                              .AllowAnyMethod()
                              .AllowAnyHeader()
                              .AllowCredentials();
                    }
                    else
                    {
                        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
                        if (allowedOrigins.Length == 0)
                        {
                            // 本番環境で origins が未設定の場合は環境変数から取得
                            var envOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
                            allowedOrigins = envOrigins?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
                        }

                        if (allowedOrigins.Length > 0)
                        {
                            policy.WithOrigins(allowedOrigins)
                                  .AllowAnyMethod()
                                  .AllowAnyHeader()
                                  .AllowCredentials();
                        }
                        else
                        {
                            // 設定がない場合は同じオリジンのみ許可
                            policy.SetIsOriginAllowed(_ => false);
                        }
                    }
                });
            });

            builder.Services.AddAuthorization(options =>
            {
                AuthorityScanService authorityScanService = new AuthorityScanService();
                foreach(var auth in authorityScanService.Authority)
                {
                    options.AddPolicy(auth, policy =>
                    policy.Requirements.Add(new DynamicRoleRequirement(auth)));
                }


            });
            builder.Services.AddAuthentication(option =>
            {
                option.DefaultScheme = IdentityConstants.ApplicationScheme;
                option.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

            builder.Services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Stores.MaxLengthForKeys = 128;
                o.User.RequireUniqueEmail = false;
            })
                .AddDefaultTokenProviders()
                .AddRoles<ApplicationRole>()
                .AddUserManager<UserManager<ApplicationUser>>()
                .AddSignInManager<SignInManager<ApplicationUser>>()
                .AddErrorDescriber<JapaneseIdentityErrorDescriber>()
                .AddEntityFrameworkStores<ApplicationDbContext>();

            var app = builder.Build();

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });
            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            app.UseCors("AllowAll");
            app.UseWebSockets();

            // 開発環境ではHTTPSリダイレクトを無効化
            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            app.UseStaticFiles();
            app.UseJsxCore();
            app.UseRouting();


            app.UseAuthentication();

            app.UseAuthorization();

            app.MapControllers();
            app.MapHub<QueueHub>("/api/queueHub");
            // JsxCore View ルーティング(SvelteKit から全面移行)
            app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", new { }, RenderMode.ServerAndClient));
            app.MapGet("/initial", () => Results.Extensions.Jsx("Initial/Index", new { }, RenderMode.ServerAndClient));
            app.MapGet("/login", () => Results.Extensions.Jsx("Login/Index", new { }, RenderMode.ServerAndClient));
            app.MapGet("/roles", () => Results.Extensions.Jsx("Roles/Index", new { }, RenderMode.ServerAndClient));
            app.MapGet("/users", () => Results.Extensions.Jsx("Users/Index", new { }, RenderMode.ServerAndClient));
            app.MapGet("/users/{username}", (string username) => Results.Extensions.Jsx("Users/Detail", new { username }, RenderMode.ServerAndClient));
            app.MapGet("/admin/delete-data", () => Results.Extensions.Jsx("Admin/DeleteData", new { }, RenderMode.ServerAndClient));
            app.MapGet("/event", () => Results.Extensions.Jsx("Event/Index", new { }, RenderMode.ServerAndClient));
            app.MapGet("/event/{eventid}", (string eventid) => Results.Extensions.Jsx("Event/Detail", new { eventId = eventid }, RenderMode.ServerAndClient));
            app.MapGet("/event/{eventid}/publishing", (string eventid) => Results.Extensions.Jsx("Event/Publishing", new { eventId = eventid }, RenderMode.ServerAndClient));
            app.MapGet("/event/{eventid}/tickets", (string eventid) => Results.Extensions.Jsx("Event/Tickets", new { eventId = eventid }, RenderMode.ServerAndClient));
            app.MapGet("/ticket/{ticketid}", (string ticketid) => Results.Extensions.Jsx("Ticket/Index", new { ticketId = ticketid }, RenderMode.ServerAndClient));
            using (var sp = app.Services.CreateScope())
            {
                var dbContext = sp.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                dbContext.Database.Migrate();
                var authorityScanService = sp.ServiceProvider.GetRequiredService<IAuthorityScanService>();

                var role =  dbContext.Roles.Where(x => x.Name == "Admin")
                    .Include(x => x.Authorities).FirstOrDefault();
                if(role != null)
                {
                    var nothave = authorityScanService.Authority.Where(x => !role.Authorities.Any(y => y.Name == x));
                    foreach(var auth in nothave)
                    {
                        Authority authority = new Authority()
                        {
                            Name = auth,
                            RoleId = role.Id,
                            Role = role
                        };
                        role.Authorities.Add(authority);
                        dbContext.Add(authority);
                        dbContext.SaveChanges();
                    }
                }
            }
            app.Run();
        }
    }
}
