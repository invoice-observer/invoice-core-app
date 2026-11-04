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


// Fails startup when InvoiceService is missing or unknown.
var invoiceServiceMode = InvoiceServiceMode.Parse(builder.Configuration["InvoiceService"]);
if (invoiceServiceMode == InvoiceServiceMode.SqlServer)
{
    // Built here, not in the options callback, so missing values fail startup.
    var connectionString = DatabaseSettings.BuildConnectionString(builder.Configuration);
    builder.Services.AddDbContext<InvoiceDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddScoped<IInvoiceService, EfInvoiceService>();

    // Configure RabbitMQ
    builder.Services.Configure<RmqSettings>(builder.Configuration.GetSection("RabbitMQ"));
    builder.Services.AddSingleton<IConnectionFactory>(sp =>
    {
        var rmqSettings = sp.GetRequiredService<IOptions<RmqSettings>>().Value;
        return new ConnectionFactory
        {
            Uri = new Uri(rmqSettings.ConnectionString),
            UserName = rmqSettings.UserName,
            Password = rmqSettings.UserPassword,
            AutomaticRecoveryEnabled = false
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

// Fails startup when Auth:UserName, Auth:Password or Auth:JwtKey is missing.
var authSettings = AuthSettings.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(authSettings);
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
            IssuerSigningKey = authSettings.SigningKey
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
