using DmapShared.Api;
using System.Security.Cryptography;
using System.Text;

namespace DmapApi;

/// <summary>Checks access keys. Each key carries its own role.</summary>
public sealed class ApiKeys(ServerOptions options)
{
    private readonly List<(byte[] Key, string Role)> _keys =
        options.Keys.Select(k => (Encoding.UTF8.GetBytes(k.Key), k.Role)).ToList();

    /// <summary>The role for <paramref name="supplied"/>, or null if the key isn't accepted.</summary>
    public string? RoleFor(string? supplied)
    {
        if (string.IsNullOrEmpty(supplied)) return null;
        var bytes = Encoding.UTF8.GetBytes(supplied);
        string? role = null;
        foreach (var (key, keyRole) in _keys)
            if (CryptographicOperations.FixedTimeEquals(bytes, key))
                role ??= keyRole;
        return role;
    }
}

/// <summary>Every /api request needs an accepted X-Api-Key.</summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, ApiKeys keys)
{
    public const string RoleItem = "DmapRole";
    public const string UserItem = "DmapUser";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        string? role = keys.RoleFor(context.Request.Headers[ApiHeaders.ApiKey].FirstOrDefault());
        if (role == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "The access key wasn't accepted." });
            return;
        }

        context.Items[RoleItem] = role;
        context.Items[UserItem] = UserNameFrom(context.Request.Headers[ApiHeaders.UserName].FirstOrDefault());
        await next(context);
    }

    /// <summary>The display name sent by the app (only shown on pages; it isn't a login).</summary>
    private static string UserNameFrom(string? header)
    {
        string name;
        try
        {
            name = Uri.UnescapeDataString(header ?? "").Trim();
        }
        catch (UriFormatException)
        {
            name = "";
        }
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length > 80) name = name[..80];
        return name.Length > 0 ? name : "Someone";
    }

    public static string Role(HttpContext context) => context.Items[RoleItem] as string ?? ApiRoles.Viewer;
    public static string User(HttpContext context) => context.Items[UserItem] as string ?? "Someone";
}
