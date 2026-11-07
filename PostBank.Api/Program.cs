using Microsoft.OpenApi.Models;
using Nexus.Bundles;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

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
app.UseNexusPlatform();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapNexusPlatform();
app.MapNexusProjectManagement();

// Seed and schema (Database:SeedOnStartup). Chat's additive schema patch runs regardless of it.
await app.InitializeNexusPlatformAsync();
await app.InitializeNexusProjectManagementAsync();

app.Run();
