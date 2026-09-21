using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api;
using DarkMyst.Api.Accounts;
using DarkMyst.Api.Battles;
using DarkMyst.Api.Content;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using DarkMyst.Api.Debug;
using DarkMyst.Api.Evolve;
using DarkMyst.Api.Expeditions;
using DarkMyst.Api.Idempotency;
using DarkMyst.Api.Ledger;
using DarkMyst.Api.Teams;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Shared with IdempotencyService so a stored/replayed response serializes with exactly the same
// casing and enum handling as a fresh one (see that class's constructor remarks).
builder.Services.AddSingleton(jsonOptions);

string connectionString = builder.Configuration.GetConnectionString("ApiDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ApiDb is not configured.");

builder.Services.AddDbContext<ApiDbContext>(o => o
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(sp => ContentPackRegistry.LoadSingleDirectory(
    ContentLocator.Locate(builder.Configuration), sp.GetRequiredService<ILogger<ContentPackRegistry>>()));

builder.Services.AddSingleton<IIdentityProvider, StubIdentityProvider>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<LinkingService>();
builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<EvolveService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<ExpeditionService>();
builder.Services.AddScoped<BattleService>();

var app = builder.Build();

// Applying migrations at startup (rather than requiring a separate operational step) is the
// simplest thing that satisfies acceptance criterion 4 ("migrations apply cleanly to an empty
// database") for a phase-D alpha with no deployment pipeline yet — see docs/10-backend-spec.md
// "deliberately deferred" for why this is not the answer for a real release.
using (IServiceScope scope = app.Services.CreateScope())
{
    ApiDbContext db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    Exception error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    (int status, object body) = ApiErrors.Map(error);
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(body, jsonOptions);
}));

// ---------------------------------------------------------------------------
// Accounts
// ---------------------------------------------------------------------------

app.MapPost("/accounts/guest", async (AccountService accounts, CancellationToken ct) =>
{
    AccountEntity account = await accounts.CreateGuestAsync(ct);
    return Results.Ok(new { accountId = account.Id, accessToken = account.AccessToken });
});

app.MapGet("/accounts/me", async (HttpContext http, ApiDbContext db, AccountService accounts, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    AccountSaveSummary summary = await accounts.SummarizeAsync(account.Id, ct);
    return Results.Ok(new { accountId = account.Id, kind = account.Kind.ToString(), gold = account.Gold, summary });
});

app.MapPost("/accounts/link/start", async (HttpContext http, ApiDbContext db, LinkingService linking,
    IIdentityProvider identityProvider, IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (LinkStartRequest request, string raw) = await ApiIo.ReadBodyAsync<LinkStartRequest>(http.Request, jsonOptions, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "accounts/link/start", key, raw, async () =>
    {
        ExternalIdentity identity = identityProvider.Resolve(request.Provider, request.ExternalToken);
        LinkOutcome result = await linking.StartAsync(account.Id, identity, ct);
        return result switch
        {
            LinkOutcome.Linked l => new IdempotentOperationResult(200, new { status = "linked", accountId = l.AccountId }),
            LinkOutcome.ConflictPending c => new IdempotentOperationResult(200, new
            {
                status = "conflict",
                pendingLinkId = c.PendingLinkId,
                guestSave = c.GuestSave,
                existingSave = c.ExistingSave
            }),
            _ => throw new InvalidOperationException("Unhandled LinkOutcome.")
        };
    }, ct);

    return ApiIo.ToResult(outcome);
});

app.MapPost("/accounts/link/confirm", async (HttpContext http, ApiDbContext db, LinkingService linking,
    IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (LinkConfirmRequest request, string raw) = await ApiIo.ReadBodyAsync<LinkConfirmRequest>(http.Request, jsonOptions, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "accounts/link/confirm", key, raw, async () =>
    {
        AccountEntity winner = await linking.ConfirmAsync(request.PendingLinkId, request.Choice, ct);
        // See docs/10-backend-spec.md "deliberately deferred": there is no separate
        // login-via-identity-provider endpoint in this round, so the winner's own bearer token is
        // handed back here directly — the only place a client on the losing side of a link can
        // otherwise obtain it.
        return new IdempotentOperationResult(200, new { accountId = winner.Id, accessToken = winner.AccessToken });
    }, ct);

    return ApiIo.ToResult(outcome);
});

// ---------------------------------------------------------------------------
// Evolve
// ---------------------------------------------------------------------------

app.MapPost("/evolve/preview", async (HttpContext http, ApiDbContext db, EvolveService evolve, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (EvolveRequest request, _) = await ApiIo.ReadBodyAsync<EvolveRequest>(http.Request, jsonOptions, ct);
    EvolvePreviewResponse preview = await evolve.PreviewAsync(account.Id, request, ct);
    return Results.Ok(preview);
});

app.MapPost("/evolve/confirm", async (HttpContext http, ApiDbContext db, EvolveService evolve,
    IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (EvolveRequest request, string raw) = await ApiIo.ReadBodyAsync<EvolveRequest>(http.Request, jsonOptions, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "evolve/confirm", key, raw, async () =>
    {
        IdempotentOperationResultOrRefusal result = await evolve.EvolveAsync(account.Id, request, key, ct);
        return result.IsSuccess
            ? new IdempotentOperationResult(200, result.Success)
            : new IdempotentOperationResult(409, new { error = "evolve_refused", blockers = result.Blockers });
    }, ct);

    return ApiIo.ToResult(outcome);
});

// ---------------------------------------------------------------------------
// Teams
// ---------------------------------------------------------------------------

app.MapPost("/teams", async (HttpContext http, ApiDbContext db, TeamService teams,
    IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (SaveTeamRequest request, string raw) = await ApiIo.ReadBodyAsync<SaveTeamRequest>(http.Request, jsonOptions, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "teams/create", key, raw, async () =>
    {
        SavedTeamResponse response = await teams.CreateAsync(account.Id, request, ct);
        return new IdempotentOperationResult(200, response);
    }, ct);

    return ApiIo.ToResult(outcome);
});

app.MapDelete("/teams/{teamId}", async (string teamId, HttpContext http, ApiDbContext db, TeamService teams,
    IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "teams/delete:" + teamId, key, teamId, async () =>
    {
        await teams.DeleteAsync(account.Id, teamId, ct);
        await teams.ReleaseIfUnusedElsewhereAsync(new[] { teamId }, ct);
        return new IdempotentOperationResult(200, new { deleted = teamId });
    }, ct);

    return ApiIo.ToResult(outcome);
});

// ---------------------------------------------------------------------------
// Expeditions
// ---------------------------------------------------------------------------

app.MapPost("/expeditions/start", async (HttpContext http, ApiDbContext db, ExpeditionService expeditions,
    IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (StartExpeditionRequest request, string raw) = await ApiIo.ReadBodyAsync<StartExpeditionRequest>(http.Request, jsonOptions, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "expeditions/start", key, raw, async () =>
    {
        ExpeditionRunSummary summary = await expeditions.StartAsync(account.Id, request, ct);
        return new IdempotentOperationResult(200, summary);
    }, ct);

    return ApiIo.ToResult(outcome);
});

app.MapPost("/expeditions/{runId}/choose", async (string runId, HttpContext http, ApiDbContext db,
    ExpeditionService expeditions, IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (ChooseRequest request, string raw) = await ApiIo.ReadBodyAsync<ChooseRequest>(http.Request, jsonOptions, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "expeditions/choose:" + runId, key, raw, async () =>
    {
        ChooseResponse response = await expeditions.ChooseAsync(account.Id, runId, request, ct);
        return new IdempotentOperationResult(200, response);
    }, ct);

    return ApiIo.ToResult(outcome);
});

app.MapPost("/expeditions/{runId}/abandon", async (string runId, HttpContext http, ApiDbContext db,
    ExpeditionService expeditions, IdempotencyService idempotency, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    string key = ApiIo.RequireIdempotencyKey(http.Request);

    IdempotencyOutcome outcome = await idempotency.ExecuteAsync(account.Id, "expeditions/abandon:" + runId, key, runId, async () =>
    {
        ExpeditionRunSummary summary = await expeditions.AbandonAsync(account.Id, runId, ct);
        return new IdempotentOperationResult(200, summary);
    }, ct);

    return ApiIo.ToResult(outcome);
});

app.MapGet("/expeditions/{runId}", async (string runId, HttpContext http, ApiDbContext db,
    ExpeditionService expeditions, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    ExpeditionRunSummary summary = await expeditions.GetAsync(account.Id, runId, ct);
    return Results.Ok(summary);
});

// ---------------------------------------------------------------------------
// Training-ground battle
// ---------------------------------------------------------------------------

app.MapPost("/battle/run", async (HttpContext http, ApiDbContext db, BattleService battles, CancellationToken ct) =>
{
    AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
    (RunBattleRequest request, _) = await ApiIo.ReadBodyAsync<RunBattleRequest>(http.Request, jsonOptions, ct);
    RunBattleResponse response = await battles.RunAsync(account.Id, request, ct);
    return Results.Ok(response);
});

// ---------------------------------------------------------------------------
// Debug-only grants — stand in for the shop/gacha system docs/00-overview.md defers to phase E.
// Gated off outside Development so this never ships as a real endpoint.
// ---------------------------------------------------------------------------

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Debug:AllowGrants"))
{
    app.MapPost("/debug/grant-character", async (HttpContext http, ApiDbContext db, LedgerService ledger,
        ContentPackRegistry content, CancellationToken ct) =>
    {
        AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
        (GrantCharacterRequest request, _) = await ApiIo.ReadBodyAsync<GrantCharacterRequest>(http.Request, jsonOptions, ct);
        OwnedCharacterEntity granted = await DebugGrants.GrantCharacterAsync(db, content, ledger, account.Id, request, ct);
        return Results.Ok(granted.ToModel());
    });

    app.MapPost("/debug/grant-gold", async (HttpContext http, ApiDbContext db, LedgerService ledger, CancellationToken ct) =>
    {
        AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
        (GrantGoldRequest request, _) = await ApiIo.ReadBodyAsync<GrantGoldRequest>(http.Request, jsonOptions, ct);
        await DebugGrants.GrantGoldAsync(db, ledger, account.Id, request.Amount, ct);
        return Results.Ok(new { granted = request.Amount });
    });

    app.MapPost("/debug/grant-material", async (HttpContext http, ApiDbContext db, LedgerService ledger, CancellationToken ct) =>
    {
        AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
        (GrantMaterialRequest request, _) = await ApiIo.ReadBodyAsync<GrantMaterialRequest>(http.Request, jsonOptions, ct);
        await DebugGrants.GrantMaterialAsync(ledger, account.Id, request.MaterialId, request.Amount, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { granted = request.MaterialId, amount = request.Amount });
    });

    // Read-only lookup the test suite (and manual curl exploration) uses to assert a character's
    // post-operation state — never exposed outside Development/Debug:AllowGrants, same as the
    // grant endpoints above.
    app.MapGet("/debug/character/{instanceId}", async (string instanceId, HttpContext http, ApiDbContext db, CancellationToken ct) =>
    {
        AccountEntity account = await AccountAuth.RequireAccountAsync(http, db, ct);
        OwnedCharacterEntity character = await db.Characters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.InstanceId == instanceId, ct);

        return character == null || character.OwnerId != account.Id
            ? Results.NotFound()
            : Results.Ok(character.ToModel());
    });
}

app.Run();

/// <summary>Exposed so tests/DarkMyst.Api.Tests can boot the same app via WebApplicationFactory.</summary>
public partial class Program
{
}
