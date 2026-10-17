using DmapShared.Api;
using DmapShared.Storage;
using System.Text.RegularExpressions;

namespace DmapApi;

/// <summary>
/// The server's endpoints, all under /api/dmap and all needing an access key:
///   GET    health                     is the server up, and what can this key do
///   GET    guide                      every page, and the guide version
///   GET    changes?since=N            pages changed or deleted after guide version N
///   PUT    records/{kind}/{id}        save a page (409 + the current page if someone saved it first)
///   DELETE records/{kind}/{id}        delete a page and the pages inside it
///   GET/PUT/DELETE locks/{id}         "someone is editing this page"
///   POST   images?ext=.jpg            add a picture (the body is the file)
///   GET    images/{name}              fetch a picture
/// </summary>
public static partial class Endpoints
{
    private const long MaxImageBytes = 15 * 1024 * 1024;
    private static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    [GeneratedRegex("^[0-9a-f]{32}\\.(png|jpg|jpeg|gif|bmp|webp)$")]
    private static partial Regex ImageName();

    public static void MapGuideEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/dmap");

        api.MapGet("/health", (HttpContext context, GuideDatabase db) => Results.Ok(new ServerInfo
        {
            Status = "ok",
            Role = ApiKeyMiddleware.Role(context),
            Version = db.GetChanges(long.MaxValue).Version
        }));

        api.MapGet("/guide", (GuideDatabase db) => Results.Ok(db.GetGuide()));

        api.MapGet("/changes", (long? since, GuideDatabase db) => Results.Ok(db.GetChanges(since ?? 0)));

        api.MapPut("/records/{kind}/{id}", (HttpContext context, string kind, string id, RecordWrite write, GuideDatabase db) =>
        {
            if (!CanEdit(context)) return ReadOnly();
            id = NormalizeId(kind, id);
            write.ParentId = write.ParentId is { } parent ? NormalizeId(GuideRecords.Season, parent) : null;

            var result = db.Put(kind, id, write, ApiKeyMiddleware.User(context));
            if (result.Problem != null) return Results.BadRequest(new ErrorResponse { Error = result.Problem });
            if (result.IsGone) return Results.NotFound(new ErrorResponse { Error = "That page no longer exists." });
            if (result.IsConflict)
                return Results.Conflict(new ConflictResponse { Error = "Someone else saved this page first.", Current = result.Record });
            return result.IsCreated
                ? Results.Created($"/api/dmap/records/{kind}/{id}", result.Record)
                : Results.Ok(result.Record);
        });

        api.MapDelete("/records/{kind}/{id}", (HttpContext context, string kind, string id, GuideDatabase db) =>
        {
            if (!CanEdit(context)) return ReadOnly();
            return db.Delete(kind, NormalizeId(kind, id)) switch
            {
                DeleteResult.Deleted => Results.NoContent(),
                DeleteResult.NotFound => Results.NotFound(new ErrorResponse { Error = "That page no longer exists." }),
                _ => Results.BadRequest(new ErrorResponse { Error = "That page can't be deleted." })
            };
        });

        api.MapGet("/locks/{id}", (string id, EditLocks locks) =>
            locks.Get(id) is { } held ? Results.Ok(held) : Results.NoContent());

        api.MapPut("/locks/{id}", (HttpContext context, string id, EditLockMessage message, EditLocks locks) =>
        {
            if (!CanEdit(context)) return ReadOnly();
            if (message.Session == Guid.Empty) return Results.BadRequest(new ErrorResponse { Error = "A session is required." });
            message.User = ApiKeyMiddleware.User(context);
            locks.Set(id, message);
            return Results.NoContent();
        });

        api.MapDelete("/locks/{id}", (HttpContext context, string id, Guid session, EditLocks locks) =>
        {
            if (!CanEdit(context)) return ReadOnly();
            locks.Release(id, session);
            return Results.NoContent();
        });

        api.MapPost("/images", async (HttpContext context, string? ext, ServerOptions options) =>
        {
            if (!CanEdit(context)) return ReadOnly();
            ext = (ext ?? "").Trim().ToLowerInvariant();
            if (!ext.StartsWith('.')) ext = "." + ext;
            if (!ImageTypes.Contains(ext))
                return Results.BadRequest(new ErrorResponse { Error = "Pictures must be PNG, JPEG, GIF, BMP or WebP files." });
            if (context.Request.ContentLength > MaxImageBytes)
                return Results.BadRequest(new ErrorResponse { Error = "That picture is too large (the limit is 15 MB)." });

            Directory.CreateDirectory(options.ImagesFolder);
            string name = $"{Guid.NewGuid():N}{ext}";
            string path = Path.Combine(options.ImagesFolder, name);
            string tmp = path + ".upload";

            long written = 0;
            await using (var file = File.Create(tmp))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await context.Request.Body.ReadAsync(buffer)) > 0)
                {
                    written += read;
                    if (written > MaxImageBytes)
                    {
                        file.Close();
                        File.Delete(tmp);
                        return Results.BadRequest(new ErrorResponse { Error = "That picture is too large (the limit is 15 MB)." });
                    }
                    await file.WriteAsync(buffer.AsMemory(0, read));
                }
            }

            if (written == 0)
            {
                File.Delete(tmp);
                return Results.BadRequest(new ErrorResponse { Error = "The picture was empty." });
            }

            File.Move(tmp, path);
            return Results.Ok(new ImageUploadResult { Path = $"images/{name}" });
        });

        api.MapGet("/images/{name}", (string name, ServerOptions options) =>
        {
            name = name.ToLowerInvariant();
            if (!ImageName().IsMatch(name)) return Results.NotFound();
            string path = Path.Combine(options.ImagesFolder, name);
            if (!File.Exists(path)) return Results.NotFound();
            string type = Path.GetExtension(name) switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            return Results.File(path, type);
        });
    }

    private static bool CanEdit(HttpContext context) => ApiRoles.CanEdit(ApiKeyMiddleware.Role(context));

    private static IResult ReadOnly() =>
        Results.Json(new ErrorResponse { Error = "This access key can only read the guide." }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>One spelling per GUID, so the same page can't end up stored twice.</summary>
    private static string NormalizeId(string kind, string id) =>
        kind != GuideRecords.Guide && Guid.TryParse(id, out var guid) ? GuideRecords.IdText(guid) : id;
}
