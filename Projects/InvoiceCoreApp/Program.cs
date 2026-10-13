using System.Text;
using DataLayer;
using DataLayer.Services;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();


var invoiceServiceType = builder.Configuration["InvoiceService"];
if (string.Equals(invoiceServiceType, "SQLite", StringComparison.InvariantCultureIgnoreCase))
{
    builder.Services.AddDbContext<InvoiceDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("InvoiceDb")));
    builder.Services.AddScoped<IInvoiceService, SqLiteInvoiceService>();

    // Configure RabbitMQ
    builder.Services.Configure<RmqSettings>(builder.Configuration.GetSection("RabbitMQ"));
    builder.Services.AddSingleton<IConnectionFactory>(sp =>
    {
        var rmqSettings = sp.GetRequiredService<IOptions<RmqSettings>>().Value;
        return new ConnectionFactory
        {
            Uri = new Uri(rmqSettings.ConnectionString),
            UserName = rmqSettings.UserName,
            Password = rmqSettings.UserPassword
        };
    });
    builder.Services.AddRmqPublisher();
    builder.Services.AddScoped(sp => new InvoiceWriteStatus(sp.GetRequiredService<IMessageReadiness>()));

}
else
{
    builder.Services.AddSingleton<IInvoiceService, MockInvoiceService>();
    builder.Services.AddScoped(_ => new InvoiceWriteStatus());
}

const string key = "super_secret_jwt_key_12345_super_secret_key";
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults
            .AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults
            .AuthenticationScheme;
    })
    .AddPolicyScheme("InvoiceStatus", null, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme : CookieAuthenticationDefaults.AuthenticationScheme;
        options.ForwardChallenge = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/Login"; // Redirect to login page if not authenticated
    }).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();
