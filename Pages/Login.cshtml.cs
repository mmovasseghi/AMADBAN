using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Amadban.Pages;

[AllowAnonymous]
[EnableRateLimiting("login")]
public sealed class LoginModel(Store store) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Banner { get; private set; }

    public void OnGet()
    {
        if (Request.Query.ContainsKey("stale"))
            Banner = "نشست فرم منقضی شده یا کوکی امنیتی مرورگر ارسال نشد. صفحه را یک‌بار تازه (F5) کنید و دوباره وارد شوید.";
        else if (Request.Query.ContainsKey("busy"))
            Banner = "تعداد تلاش ورود بیش از حد بود؛ حدود یک دقیقه صبر کنید و دوباره تلاش کنید.";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var username = (Username ?? "").Trim().ToLowerInvariant();
        var user = username.Length is > 0 and <= 100 && Password?.Length is > 0 and <= 256
            ? await store.AuthenticateAsync(username, Password)
            : null;
        if (user is null)
        {
            ModelState.AddModelError("", "نام کاربری یا رمز ورود درست نیست.");
            return Page();
        }
        var identity = new ClaimsIdentity(
            [new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.Name),
             new(ClaimTypes.Role, user.Role), new("session_stamp", user.SecurityStamp.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return RedirectToPage("/Index");
    }
}
