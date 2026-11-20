using Amadban.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using System.Threading.RateLimiting;

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

var app = builder.Build();
app.UseExceptionHandler("/Error");
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
    await next();
});
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
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
