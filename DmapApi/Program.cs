using DmapApi;

// The DMAP guide server: keeps the shared guide (one record per page) in SQLite,
// plus the pictures and "someone is editing this page" markers.
var builder = WebApplication.CreateBuilder(args);

if (builder.Configuration.GetValue<bool>("Dmap:LoadUserSecrets"))
    builder.Configuration.AddUserSecrets<Program>(optional: true);

var options = ServerOptions.From(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<GuideDatabase>();
builder.Services.AddSingleton<EditLocks>();
builder.Services.AddSingleton<ApiKeys>();

var app = builder.Build();

// Open (and if needed create or upgrade) the database before taking requests.
app.Services.GetRequiredService<GuideDatabase>();

app.UseMiddleware<ApiKeyMiddleware>();
app.MapGuideEndpoints();
app.Run();

public partial class Program;
