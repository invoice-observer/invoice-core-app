using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceCoreApp.Pages
{
    public class LoginModel : PageModel
    {
        [BindProperty]
        public string Username { get; set; } = string.Empty;
        [BindProperty]
        public string Password { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://localhost:7052");
            var response = await client.PostAsJsonAsync("/api/auth/login", new { Username, Password });
            if (response.IsSuccessStatusCode)
            {   
                var result = await response.Content.ReadFromJsonAsync<LoginResult>();
                if (result?.token != null)
                {
                    var claims = new[] {
                        new Claim(ClaimTypes.Name, Username),
                        new Claim(ClaimTypes.Role, "Admin"),
                        new Claim("JWT", result.token)
                    };
                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                    return RedirectToPage("/Index");
                }
            }
            ErrorMessage = "Invalid login attempt.";
            return Page();
        }

        internal class LoginResult
        {
            // ReSharper disable once InconsistentNaming
            public string token { get; init; } = string.Empty;
        }
    }
}
