
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
using System.Security.Claims;
using QRQueue.Hubs;
using Microsoft.AspNetCore.HttpOverrides;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using JsxCore;
using JsxCore.Hosting;
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
                // enum は全 API で文字列化する(GroupStatus など。フロントの TS 型も union 型で一致させる)
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            // [ApiController] の自動 400(ProblemDetails)も { message } 形式に統一する
            builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
                o.InvalidModelStateResponseFactory = ctx => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                    new Models.API.ApiMessage(string.Join(" ", ctx.ModelState.Values
                        .SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m))))));
            builder.Services.AddScoped<IPasscodeService, PasscodeService>();
            builder.Services.AddScoped<ITicketPdfGenerator, TicketPdfGenerator>();
            builder.Services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
            // QR に埋める BaseURL 解決の共通化(設計§8)
            builder.Services.AddSingleton<IBaseUrlResolver, BaseUrlResolver>();
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

            // 参加者向け 署名付き participantToken cookie(設計§5.2.1)。既定は Identity のまま別スキーム
            builder.Services.AddAuthentication()
            .AddCookie("Participant", options =>
            {
                options.Cookie.Name = "participant";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(90);
                options.SlidingExpiration = false;
                options.Events.OnValidatePrincipal = async context =>
                {
                    // participantToken claim ⇔ DB 照合(失効=有効な参加が無い cookie は拒否)
                    var claimValue = context.Principal?.FindFirstValue("participantToken");
                    if (!Guid.TryParse(claimValue, out var token))
                    {
                        context.RejectPrincipal();
                        return;
                    }
                    var tickets = context.HttpContext.RequestServices
                        .GetRequiredService<ITicketRepository>();
                    if (!await tickets.HasActiveTicketAsync(token))
                    {
                        context.RejectPrincipal();
                    }
                };
            });

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
