
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
using Microsoft.Extensions.Caching.Memory;
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

            // JsxCore: TSX/JSX 繝薙Η繝ｼ繧ｨ繝ｳ繧ｸ繝ｳ(Node.js 荳崎ｦ・
            builder.AddJsxCore(options =>
            {
                options.Document.Language = "ja";
            });

            // Add services to the container.

            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                // enum 縺ｯ蜈ｨ API 縺ｧ譁・ｭ怜・蛹悶☆繧・GroupStatus 縺ｪ縺ｩ縲ゅヵ繝ｭ繝ｳ繝医・ TS 蝙九ｂ union 蝙九〒荳閾ｴ縺輔○繧・
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            // [ApiController] 縺ｮ閾ｪ蜍・400(ProblemDetails)繧・{ message } 蠖｢蠑上↓邨ｱ荳縺吶ｋ
            builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
                o.InvalidModelStateResponseFactory = ctx => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                    new Models.API.ApiMessage(string.Join(" ", ctx.ModelState.Values
                        .SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m))))));
            builder.Services.AddScoped<IPasscodeService, PasscodeService>();
            builder.Services.AddScoped<ITicketStatusService, TicketStatusService>();
            builder.Services.AddSingleton<ICheckinCodeService, CheckinCodeService>();
            builder.Services.AddScoped<ITicketPdfGenerator, TicketPdfGenerator>();
            builder.Services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
            // QR 縺ｫ蝓九ａ繧・BaseURL 隗｣豎ｺ縺ｮ蜈ｱ騾壼喧(險ｭ險域嶌)
            builder.Services.AddSingleton<IBaseUrlResolver, BaseUrlResolver>();
            builder.Services.AddScoped<IGroupNumberIssuanceService, GroupNumberIssuanceService>();
            builder.Services.AddScoped<IQueueCallService, QueueCallService>();
            builder.Services.AddSingleton<IVapidService, VapidService>();
            builder.Services.AddSingleton<IPushSubscriptionService, PushSubscriptionService>();
            builder.Services.AddHttpClient();
            builder.Services.AddScoped<ILineService, LineService>();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSingleton<IAuthorityScanService, AuthorityScanService>();
            builder.Services.AddSwaggerGen();
            builder.Services.AddScoped<IAuthorizationHandler, DynamicRoleHandler>();
            builder.Services.AddMemoryCache();
            builder.Services.AddSignalR();
            // DbContext プーリング: リクエスト毎のコンテキスト生成コストを削減
            // (ApplicationDbContext にメンバ状態を持たせないよう注意)
            builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("lottery-db"));
            });
            // Repository螻､(繧ｳ繝ｳ繝医Ο繝ｼ繝ｩ繝ｻ繧ｵ繝ｼ繝薙せ縺ｯ縺薙ｌ邨檎罰縺ｧ繝・・繧ｿ繧｢繧ｯ繧ｻ繧ｹ縺吶ｋ)
            builder.Services.AddScoped<IEventRepository, EventRepository>();
            builder.Services.AddScoped<IParticipationGroupRepository, ParticipationGroupRepository>();
            builder.Services.AddScoped<ITicketRepository, TicketRepository>();

            // CORS險ｭ螳・ 髢狗匱迺ｰ蠅・・蜈ｨ險ｱ蜿ｯ縲∵悽逡ｪ縺ｯappsettings.json縺九ｉ蜿門ｾ・
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
                            // 譛ｬ逡ｪ迺ｰ蠅・〒 origins 縺梧悴險ｭ螳壹・蝣ｴ蜷医・迺ｰ蠅・､画焚縺九ｉ蜿門ｾ・
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
                            // 險ｭ螳壹′縺ｪ縺・ｴ蜷医・蜷後§繧ｪ繝ｪ繧ｸ繝ｳ縺ｮ縺ｿ險ｱ蜿ｯ
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

            // 蜿ょ刈閠・髄縺・鄂ｲ蜷堺ｻ倥″ participantToken cookie(險ｭ險域嶌)縲よ里螳壹・ Identity 縺ｮ縺ｾ縺ｾ蛻･繧ｹ繧ｭ繝ｼ繝
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
                    // participantToken claim 竍・DB 辣ｧ蜷・螟ｱ蜉ｹ=譛牙柑縺ｪ蜿ょ刈縺檎┌縺・cookie 縺ｯ諡貞凄)
                    var claimValue = context.Principal?.FindFirstValue("participantToken");
                    if (!Guid.TryParse(claimValue, out var token))
                    {
                        context.RejectPrincipal();
                        return;
                    }
                    // 短時間キャッシュ: 参加者からの全リクエスト(cookie認証)でDB照会が走るため、
                    // 有効判定を30秒だけIMemoryCacheに保持する。受付取消などの反映は最大30秒遅延する。
                    var cache = context.HttpContext.RequestServices
                        .GetRequiredService<IMemoryCache>();
                    var cacheKey = $"participant-token-active:{token}";
                    var isActive = await cache.GetOrCreateAsync(cacheKey, entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                        var tickets = context.HttpContext.RequestServices
                            .GetRequiredService<ITicketRepository>();
                        return tickets.HasActiveTicketAsync(token);
                    });
                    if (!isActive)
                    {
                        // 無効判定はキャッシュせず即時破棄(再ログイン・取消を即座に反映させる)
                        cache.Remove(cacheKey);
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

            // 繝・・繝ｭ繧､(繧ｵ繝ｼ繝薙せ蜀崎ｵｷ蜍・蠕後・荳螳壽凾髢薙？TML 蠢懃ｭ斐↓ Clear-Site-Data: "cache" 繧剃ｻ倅ｸ弱＠縲・
            // 繝悶Λ繧ｦ繧ｶ菫晄戟縺ｮ蜿､縺・く繝｣繝・す繝･(莉･蜑阪・ 1蟷ｴ繧ｭ繝｣繝・す繝･縺ｮ繝薙Η繝ｼ JS 遲・繧貞ｼｷ蛻ｶ遐ｴ譽・☆繧九・
            // 縺薙ｌ縺ｫ繧医ｊ繝・・繝ｭ繧､逶ｴ蠕後・繧｢繧ｯ繧ｻ繧ｹ縺ｧ蠢・★譁ｰ縺励＞ assets 縺九ｉ隱ｭ縺ｿ逶ｴ縺帙ｋ縲・
            // 譛ｪ蟇ｾ蠢懊ヶ繝ｩ繧ｦ繧ｶ縺ｧ縺ｯ繝倥ャ繝繝ｼ縺檎┌隕悶＆繧後ｋ縺縺代〒螳ｳ縺ｯ縺ｪ縺・
            var appStartedAtUtc = DateTimeOffset.UtcNow;
            static bool IsDocumentRequest(HttpContext ctx) =>
                HttpMethods.IsGet(ctx.Request.Method) &&
                !ctx.Request.Path.StartsWithSegments("/api") &&
                !ctx.Request.Path.StartsWithSegments("/_jsx") &&
                (ctx.Request.Headers.Accept.ToString().Contains("text/html") ||
                 string.IsNullOrEmpty(ctx.Request.Headers.Accept));
            app.Use(async (context, next) =>
            {
                if (DateTimeOffset.UtcNow - appStartedAtUtc < TimeSpan.FromMinutes(10) &&
                    IsDocumentRequest(context))
                {
                    context.Response.Headers["Clear-Site-Data"] = "\"cache\"";
                }
                await next();
            });

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                // 譌｢螳壹・菫｡鬆ｼ蟇ｾ雎｡縺ｯ IPv6 繝ｫ繝ｼ繝励ヰ繝・け縺ｮ縺ｿ縲ょ酔荳繝帙せ繝医・ nginx(127.0.0.1 / ::1)邨檎罰縺ｮ
                // X-Forwarded-* 繧貞渚譏縺輔○縺ｪ縺・→縲＿R縺ｮBaseURL縺・http:// 縺ｫ縺ｪ縺｣縺溘ｊ
                // UseHttpsRedirection 縺後Μ繝繧､繝ｬ繧ｯ繝医Ν繝ｼ繝励ｒ襍ｷ縺薙☆
                KnownProxies = { System.Net.IPAddress.Parse("127.0.0.1"), System.Net.IPAddress.IPv6Loopback }
            });
            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            app.UseCors("AllowAll");
            app.UseWebSockets();

            // 髢狗匱迺ｰ蠅・〒縺ｯHTTPS繝ｪ繝繧､繝ｬ繧ｯ繝医ｒ辟｡蜉ｹ蛹・
            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            // 髱咏噪繝輔ぃ繧､繝ｫ(CSS遲・縺ｯ Cache-Control: no-cache 縺ｧ驟堺ｿ｡縺励∵ｯ主屓魄ｮ蠎ｦ讀懆ｨｼ縺輔○繧九・
            // 譌｢螳壹・縺ｾ縺ｾ(繝倥ャ繝繝ｼ辟｡縺・縺縺ｨ繝悶Λ繧ｦ繧ｶ縺ｮ繝偵Η繝ｼ繝ｪ繧ｹ繝・ぅ繝・け繧ｭ繝｣繝・す繝･縺ｫ繧医ｊ縲・
            // HTML縺ｯ譁ｰ縺励￥縺ｦCSS縺縺大商縺・憾諷九′逋ｺ逕溘☆繧九◆繧・ETag 莉倥″縺ｪ縺ｮ縺ｧ譛ｪ譖ｴ譁ｰ譎ゅ・ 304 縺ｧ鬮倬・
            // 髱咏噪繝輔ぃ繧､繝ｫ(CSS遲・縺ｮ繧ｭ繝｣繝・す繝･縺ｯ 60 遘偵↓蛻ｶ髯舌☆繧九・
            // 譌｢螳・繝倥ャ繝繝ｼ辟｡縺・縺縺ｨ繝偵Η繝ｼ繝ｪ繧ｹ繝・ぅ繝・け繧ｭ繝｣繝・す繝･縺ｧ荳榊ｮ壽悄縺ｫ蜿､縺上↑繧翫・
            // 1蟷ｴ謖・ｮ夂ｭ峨□縺ｨ繝・・繝ｭ繧､蠕後ｂ蜿､縺・ヵ繧｡繧､繝ｫ繧剃ｽｿ縺・ｶ壹￠縺ｦ謠冗判縺悟｣翫ｌ繧九◆繧・
            const string CacheControl = "public, max-age=60";
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                    ctx.Context.Response.Headers.CacheControl = CacheControl
            });
            // JsxCore 縺ｮ繝薙Η繝ｼ JS(/_jsx/ 驟堺ｸ・縺ｯ public, max-age=31536000(1蟷ｴ)縺ｧ驟堺ｿ｡縺輔ｌ繧九′縲・
            // URL 縺ｮ繝舌・繧ｸ繝ｧ繝ｳ隴伜挨蟄舌′繝・・繝ｭ繧､髢薙〒荳榊､峨・縺溘ａ縲√ヶ繝ｩ繧ｦ繧ｶ縺悟商縺・ン繝･繝ｼ JS繧剃ｽｿ縺・ｶ壹￠
            // 縲粂TML/CSS縺ｯ譁ｰ縺励￥縺ｦ謠冗判縺縺大商縺・咲憾諷九′襍ｷ縺阪ｋ縲ゅ％縺｡繧峨ｂ 60 遘偵↓蛻ｶ髯舌☆繧・
            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    if (context.Request.Path.StartsWithSegments("/_jsx"))
                    {
                        context.Response.Headers.CacheControl = CacheControl;
                    }
                    return Task.CompletedTask;
                });
                await next();
            });
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
