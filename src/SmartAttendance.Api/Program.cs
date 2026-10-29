using Microsoft.OpenApi;
using SmartAttendance.Api.Accounts;
using SmartAttendance.Api.Authentication;
using SmartAttendance.Api.Middleware;
using SmartAttendance.Api.Reports;
using SmartAttendance.Application.Accounts;
using SmartAttendance.Application.Reports;
using SmartAttendance.Infrastructure.Reporting;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]) &&
    int.TryParse(builder.Configuration["PORT"], out var platformPort) &&
    platformPort is > 0 and <= 65535)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Smart Attendance API",
        Version = "v1",
        Description = "REST API for Smart Attendance.",
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "Firebase ID Token",
        Description = "Enter a Firebase ID Token.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});
builder.Services.AddFirebaseInfrastructure(
    builder.Configuration["Firebase:ProjectId"],
    builder.Configuration["FIREBASE_AUTH_EMULATOR_HOST"],
    builder.Configuration["FIRESTORE_EMULATOR_HOST"]);
builder.Services.AddSingleton<IAccountOperationLogger, AccountOperationLogger>();
builder.Services.AddSingleton<IAccountManagementService, AccountManagementService>();
builder.Services.AddSingleton<IReportClock, SystemReportClock>();
builder.Services.AddSingleton<IReportEmailOperationLogger, ReportEmailOperationLogger>();
builder.Services.AddSingleton<IAttendanceReportEmailService, AttendanceReportEmailService>();
builder.Services.AddReportEmailInfrastructure(
    builder.Configuration,
    builder.Environment);
builder.Services.AddFirebaseApiSecurity();

var app = builder.Build();

app.UseMiddleware<AccountManagementExceptionMiddleware>();
app.UseMiddleware<AttendanceReportEmailExceptionMiddleware>();
app.UseMiddleware<FirebaseDependencyExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Smart Attendance API v1");
        options.DocumentTitle = "Smart Attendance API";
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
{
    app.MapAuthDiagnosticEndpoints();
}

app.Run();

public partial class Program;
