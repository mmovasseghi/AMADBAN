using Amadban.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using System.Threading.RateLimiting;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Amadban");
if (string.IsNullOrWhiteSpace(connection))
{
    connection = new NpgsqlConnectionStringBuilder
    {
        Host = builder.Configuration["AMADBAN_DB_HOST"] ?? "localhost",
        Database = "amadban",
        Username = "amadban",
        Password = builder.Configuration["AMADBAN_DB_PASSWORD"]
    }.ConnectionString;
}
builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddSingleton<Store>();
var protection = builder.Services.AddDataProtection().SetApplicationName("Amadban");
if (builder.Configuration["AMADBAN_KEYS"] is { Length: > 0 } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.Cookie.Name = "Amadban.Session";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/Denied";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Events.OnValidatePrincipal = async context =>
        {
            var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var stampText = context.Principal?.FindFirstValue("session_stamp");
            if (!long.TryParse(idText, out var id) || !Guid.TryParse(stampText, out var stamp) ||
                !await context.HttpContext.RequestServices.GetRequiredService<Store>().SessionValidAsync(id, stamp))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => !HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetNoLimiter("login-page")
        : RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12, Window = TimeSpan.FromMinutes(1)
            }));
});
builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/"))
    .AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddResponseCompression(options => options.EnableForHttps = false);

var app = builder.Build();
app.UseResponseCompression();
app.UseExceptionHandler("/Error");
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
    await next();
});
app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = false,
    OnPrepareResponse = context =>
    {
        var path = context.File.Name;
        if (path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
            context.Context.Response.Headers.ContentType = "font/woff2";
        context.Context.Response.Headers.CacheControl = context.Context.Request.Query.ContainsKey("v")
            ? "public,max-age=31536000,immutable" : "public,max-age=86400";
    }
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
await app.Services.GetRequiredService<Store>().InitializeAsync(
    app.Environment.ContentRootPath, builder.Configuration["AMADBAN_ADMIN_PASSWORD"]);
await app.RunAsync();
