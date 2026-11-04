using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Persistence;
using Events.Api.Endpoints;
using Events.Application;
using Events.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexus.Actions;
using Nexus.Actions.Endpoints;
using Nexus.Actions.Infrastructure;
using Nexus.Calendar;
using Nexus.Calendar.Endpoints;
using Nexus.Calendar.Infrastructure;
using Nexus.Calendar.IranianHolidays;
using Nexus.Integrations.ProjectCalendar;
using Nexus.Integrations.ProjectVisibility;
using Nexus.Integrations.ProjectDeletion;
using Nexus.Integrations.ProjectWorkflow;
using Nexus.Integrations.ProjectWorkflow.Endpoints;
using Nexus.Integrations.StrategyAlignment;
using Nexus.Integrations.StrategyAlignment.Endpoints;
using Nexus.Integrations.StrategyAlignment.Infrastructure;
using Nexus.Knowledge;
using Nexus.Knowledge.Endpoints;
using Nexus.Knowledge.Infrastructure;
using Nexus.Organization;
using Nexus.Organization.Endpoints;
using Nexus.Organization.Infrastructure;
using Nexus.Portfolio;
using Nexus.Portfolio.Endpoints;
using Nexus.ProjectManagement.Agile;
using Nexus.ProjectManagement.Agile.Endpoints;
using Nexus.ProjectManagement.Agile.Infrastructure;
using Nexus.ProjectManagement.Core;
using Nexus.ProjectManagement.Core.Endpoints;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.Deliverables;
using Nexus.ProjectManagement.Deliverables.Endpoints;
using Nexus.ProjectManagement.Deliverables.Infrastructure;
using Nexus.ProjectManagement.Contracts;
using Nexus.ProjectManagement.Contracts.Endpoints;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using Nexus.ProjectManagement.Documents;
using Nexus.Integrations.ProjectGovernance;
using Nexus.Integrations.ProjectGovernance.Endpoints;
using Nexus.ProjectManagement.Delegation;
using Nexus.ProjectManagement.Delegation.Endpoints;
using Nexus.ProjectManagement.Delegation.Infrastructure;
using Nexus.ProjectManagement.History;
using Nexus.ProjectManagement.History.Endpoints;
using Nexus.ProjectManagement.History.Infrastructure;
using Nexus.ProjectManagement.Documents.Endpoints;
using Nexus.ProjectManagement.Documents.Infrastructure;
using Nexus.ProjectManagement.Kpi;
using Nexus.ProjectManagement.Kpi.Endpoints;
using Nexus.ProjectManagement.Kpi.Infrastructure;
using Nexus.ProjectManagement.Progress;
using Nexus.ProjectManagement.Progress.Endpoints;
using Nexus.ProjectManagement.Progress.Infrastructure;
using Nexus.ProjectManagement.RiskManagement;
using Nexus.ProjectManagement.RiskManagement.Endpoints;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using Nexus.ProjectManagement.StakeholderManagement;
using Nexus.ProjectManagement.StakeholderManagement.Endpoints;
using Nexus.ProjectManagement.StakeholderManagement.Infrastructure;
using Nexus.ProjectManagement.Team;
using Nexus.ProjectManagement.Team.Endpoints;
using Nexus.ProjectManagement.Team.Infrastructure;
using Nexus.ProjectManagement.Waterfall;
using Nexus.ProjectManagement.Waterfall.Endpoints;
using Nexus.ProjectManagement.Waterfall.Infrastructure;
using Nexus.Reporting;
using Nexus.Reporting.Analytics;
using Nexus.Reporting.Endpoints;
using Nexus.StrategyManagement;
using Nexus.StrategyManagement.Endpoints;
using Nexus.StrategyManagement.Infrastructure;
using Nexus.Workflow;
using Nexus.Workflow.Endpoints;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Identity;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Security;
using Notifications.Api.Endpoints;
using Notifications.Api.Hubs;
using Notifications.Application;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Persistence;
using Serilog;
using System.Text;
using Ticketing.Api.Endpoints;
using Ticketing.Application;
using Ticketing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

// --- Application tier: NexusCore foundation, then every composed Nexus Module. Each call is
// self-contained - deleting any one of them (and its matching Infrastructure/Map* calls below)
// removes that capability entirely, with nothing left to clean up elsewhere. ---
builder.Services.AddApplication();
builder.Services.AddChatApplication();
builder.Services.AddEventsApplication();
builder.Services.AddTicketingApplication();
builder.Services.AddNotificationApplication();

builder.Services.AddOrganizationApplication();
builder.Services.AddCalendarApplication(options => options.SingleDefaultPerTenant = true);
builder.Services.AddIranianOfficialHolidays();
builder.Services.AddWorkflowApplication(o => o.RequireDesignatedApprover = true);
// The UI keeps every occurrence of a repeating action inside one action (listed from its rule), so completing one must not spawn a copy.
builder.Services.AddActionManagement(options =>
{
    options.CreateNextOccurrenceOnCompletion = false;
    options.RequireEndAfterStart = true; // the UI's rule: an action ends after it starts
});
builder.Services.AddKnowledgeManagement();
builder.Services.AddStrategyManagement();

builder.Services.AddProjectManagementCore(options => { options.ValidateCharter = true; options.RequireUniqueName = true; });
builder.Services.AddWaterfallPlanning(options =>
{
    options.ValidateActivityHierarchy = true;
    // The UI reads planned progress from the schedule (spread evenly over each activity) and rolls groups up by what is inside them.
    options.DefaultPlannedProgress = Nexus.ProjectManagement.Waterfall.Domain.PlannedProgressMode.Continuous;
    options.RollUpSummaryWeights = true;
    options.RequireWeightsTotalOnSubmit = true;
});
builder.Services.AddAgilePlanning();
builder.Services.AddProjectTeam();
builder.Services.AddProjectDeliverables();
builder.Services.AddProjectKpi();
builder.Services.AddRiskManagement();
builder.Services.AddStakeholderManagement();
builder.Services.AddProgressManagement();
builder.Services.AddProjectDocuments();
builder.Services.AddContractManagement();
builder.Services.AddProjectHistory();
builder.Services.AddItemDelegation();

builder.Services.AddProjectWorkflowIntegration();
builder.Services.AddProjectGovernance();
builder.Services.AddProjectVisibilityIntegration();
builder.Services.AddProjectDeletionIntegration();
builder.Services.AddProjectCalendarIntegration();
builder.Services.AddProjectStrategyAlignment();

builder.Services.AddPortfolio();
builder.Services.AddProjectReporting();
builder.Services.AddReportingAnalytics(o => { o.ProgressFromProjectPlan = true; o.OnlyApprovedProjects = true; });

// --- Infrastructure tier: one DbContext per module, all pointed at the same DefaultConnection
// database (isolated by schema - see each module's own ToTable(name, schema) configuration). ---
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddChatInfrastructure(builder.Configuration);
builder.Services.AddEventsInfrastructure(builder.Configuration);
builder.Services.AddTicketingInfrastructure(builder.Configuration);
builder.Services.AddNotificationInfrastructure(builder.Configuration);

builder.Services.AddOrganizationInfrastructure(builder.Configuration);
builder.Services.AddCalendarInfrastructure(builder.Configuration);
builder.Services.AddWorkflowInfrastructure(builder.Configuration);
builder.Services.AddActionManagementInfrastructure(builder.Configuration);
builder.Services.AddKnowledgeManagementInfrastructure(builder.Configuration);
builder.Services.AddStrategyManagementInfrastructure(builder.Configuration);

builder.Services.AddProjectManagementCoreInfrastructure(builder.Configuration);
builder.Services.AddWaterfallPlanningInfrastructure(builder.Configuration);
builder.Services.AddAgilePlanningInfrastructure(builder.Configuration);
builder.Services.AddProjectTeamInfrastructure(builder.Configuration);
builder.Services.AddProjectDeliverablesInfrastructure(builder.Configuration);
builder.Services.AddProjectKpiInfrastructure(builder.Configuration);
builder.Services.AddRiskManagementInfrastructure(builder.Configuration);
builder.Services.AddStakeholderManagementInfrastructure(builder.Configuration);
builder.Services.AddProgressManagementInfrastructure(builder.Configuration);
builder.Services.AddProjectDocumentsInfrastructure(builder.Configuration);
builder.Services.AddContractManagementInfrastructure(builder.Configuration);
builder.Services.AddProjectHistoryInfrastructure(builder.Configuration);
builder.Services.AddItemDelegationInfrastructure(builder.Configuration);

builder.Services.AddProjectStrategyAlignmentInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
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

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Every module's own AddXxxApplication() call above already added its own permission policies
// via its own services.AddAuthorization(...) call (additive across modules - see e.g.
// Nexus.Portfolio/DependencyInjection.cs). This one covers NexusCore's own base identity
// permissions, the one set no module owns.
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in IdentityPermissions.All)
    {
        options.AddPolicy(permission.Name, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
    }
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapChatEndpoints();
app.MapTicketEndpoints();
app.MapNotificationEndpoints();
app.MapEventEndpoints();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapIdentityEndpoints();
if (builder.Configuration.IsUserGroupFeatureEnabled())
{
    app.MapUserGroupEndpoints();
}

app.MapOrganizationEndpoints();
app.MapCalendarEndpoints();
app.MapWorkflowEndpoints();
app.MapActionEndpoints();
app.MapKnowledgeDocumentEndpoints();
app.MapStrategyEndpoints();

app.MapProjectEndpoints();
app.MapWaterfallEndpoints();
app.MapAgileTaskEndpoints();
app.MapTeamEndpoints();
app.MapDeliverableEndpoints();
app.MapKpiEndpoints();
app.MapRiskEndpoints();
app.MapStakeholderEndpoints();
app.MapProgressEndpoints();
app.MapProjectDocumentEndpoints();
app.MapContractEndpoints();
app.MapProjectHistoryEndpoints();
app.MapItemDelegationEndpoints();
app.MapProjectGovernanceEndpoints();

app.MapProjectWorkflowEndpoints();
app.MapProjectStrategyAlignmentEndpoints();

app.MapPortfolioEndpoints();
app.MapDashboardEndpoints();
app.MapReportingAnalyticsEndpoints();

if (builder.Configuration.GetValue("Database:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var cancellationToken = CancellationToken.None;

    // Seeds NexusCoreDbContext's own schema (via ModuleSchemaInitializer internally) plus the
    // default tenant/admin and every permission contributed by an installed module's
    // IPermissionCatalog. Every other module's DbContext then needs its own schema created too -
    // see ModuleSchemaInitializer's own comment for why each needs its own call rather than one
    // shared EnsureCreatedAsync.
    await services.GetRequiredService<DefaultDataSeeder>().SeedAsync(cancellationToken);

    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<OrganizationDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<CalendarDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<WorkflowDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ActionsDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<KnowledgeDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<StrategyManagementDbContext>(), cancellationToken);

    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProjectManagementCoreDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<WaterfallDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<AgileDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<TeamDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<DeliverablesDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<KpiDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<RiskManagementDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<StakeholderManagementDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProgressDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProjectDocumentsDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ContractsDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProjectHistoryDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ItemDelegationDbContext>(), cancellationToken);

    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<StrategyAlignmentDbContext>(), cancellationToken);

    // In-app notifications' schema. Chat's own schema init/patch runs unconditionally below,
    // regardless of this flag - see the comment there for why.
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<NotificationDbContext>(), cancellationToken);
}

// Chat's schema must stay current even when Database:SeedOnStartup is disabled - the setting
// docs/Core-Packaging-Government-Deployment.md tells production deployments to keep disabled
// after their initial setup. EnsureCreatedAsync/EnsureColumnAsync only ever create a missing
// table or add a missing column; they never seed or change data, so running them on every
// restart is safe regardless of that flag. Without this, a deployment that disables seeding
// (exactly as documented) would never receive a later additive Chat schema change - such as
// team conversations' TeamId column - and chat would keep failing with a 500 no matter how many
// times the fix for that change is deployed.
{
    using var chatSchemaScope = app.Services.CreateScope();
    var chatDb = chatSchemaScope.ServiceProvider.GetRequiredService<ChatDbContext>();
    var chatCancellationToken = CancellationToken.None;
    await ModuleSchemaInitializer.EnsureCreatedAsync(chatDb, chatCancellationToken);

    // Columns added to Chat's model after a deployment's Conversations table already existed
    // (team conversations): EnsureCreatedAsync above cannot add these to an existing table on
    // its own, so a live database that predates them would otherwise 500 on any query that
    // touches Conversations. Safe to call on every restart.
    await ModuleSchemaInitializer.EnsureColumnAsync(
        chatDb, "dbo.Conversations", "TeamId",
        "ALTER TABLE dbo.Conversations ADD TeamId uniqueidentifier NULL;",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_TeamId' AND object_id = OBJECT_ID(N'dbo.Conversations')) " +
        "CREATE UNIQUE INDEX IX_Conversations_TeamId ON dbo.Conversations(TeamId) WHERE TeamId IS NOT NULL;",
        chatCancellationToken);
}

app.Run();
