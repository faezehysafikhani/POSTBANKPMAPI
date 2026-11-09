using Microsoft.AspNetCore.DataProtection;
using Microsoft.OpenApi.Models;
using Nexus.Bundles;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

// The keys that encrypt stored secrets (SMS panel key, LDAP password) live in a folder the operator backs up;
// without it they stay in the app pool's profile and are lost with a new server or pool.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("PostBank.Api");
}

// A real deployment must not run with the development signing key or a short one.
var signingKey = builder.Configuration["Jwt:SigningKey"] ?? "";
if (!builder.Environment.IsDevelopment() && (signingKey.Length < 32 || signingKey.StartsWith("replace-this", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("Jwt:SigningKey must be set (environment variable Jwt__SigningKey, at least 32 characters) outside development.");
}

// --- NexusCore: the platform, then the project-management capability set. ---
builder.Services.AddNexusPlatform(builder.Configuration, builder.Environment);

builder.Services.AddNexusProjectManagement(builder.Configuration, pm =>
{
    // PostBank's product rules; everything not set here keeps the module's own default.
    pm.Project = o => { o.ValidateCharter = true; o.RequireUniqueName = true; };

    // The UI keeps every occurrence of a repeating action inside one action (listed from its rule),
    // so completing one must not spawn a copy; and an action ends after it starts.
    pm.Actions = o => { o.CreateNextOccurrenceOnCompletion = false; o.RequireEndAfterStart = true; };

    // The UI reads planned progress from the schedule (spread evenly over each activity)
    // and rolls groups up by what is inside them.
    pm.Waterfall = o =>
    {
        o.ValidateActivityHierarchy = true;
        o.DefaultPlannedProgress = Nexus.ProjectManagement.Waterfall.Domain.PlannedProgressMode.Continuous;
        o.RollUpSummaryWeights = true;
        o.RequireWeightsTotalOnSubmit = true;
    };

    pm.Analytics = o => { o.ProgressFromProjectPlan = true; o.OnlyApprovedProjects = true; };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PostBank API",
        Version = "v1",
        Description = "PostBank - a project-management product built on the Nexus Platform: NexusCore foundation plus the full project-management capability set, composed as independent modules."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a valid JWT bearer token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

// The UI is served from this same site (wwwroot): one address for pages and /api, so no CORS and the session cookie works.
var serveUi = File.Exists(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html"));
if (serveUi)
{
    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/hubs"))
        {
            headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        }
        if (context.Request.IsHttps) headers["Strict-Transport-Security"] = "max-age=31536000";
        await next();
    });
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        // The settings file is edited on the server: never cached; source maps are never served.
        OnPrepareResponse = ctx =>
        {
            if (ctx.File.Name == "runtime-config.json" || ctx.File.Name == "index.html") ctx.Context.Response.Headers["Cache-Control"] = "no-store";
        }
    });
}

app.UseNexusPlatform();

if (!serveUi) app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapNexusPlatform();
app.MapNexusProjectManagement();
if (serveUi) app.MapFallbackToFile("index.html");

// Seed and schema (Database:SeedOnStartup). Chat's additive schema patch runs regardless of it.
await app.InitializeNexusPlatformAsync();
await app.InitializeNexusProjectManagementAsync();

app.Run();
