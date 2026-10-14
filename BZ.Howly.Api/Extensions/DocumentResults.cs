using BZ.Howly.Api.Core.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BZ.Howly.Api.Extensions;

public static class DocumentResults
{
    /// <summary>
    /// Every document is downloaded as an attachment of a neutral type, with nosniff: whatever it contains, the browser
    /// saves it instead of opening or running it inside the portal.
    /// </summary>
    public static IActionResult Download(this ControllerBase controller, DocumentFile? file)
    {
        if (file is null)
            return controller.NotFound();

        controller.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        controller.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        return controller.File(file.Content, "application/octet-stream", file.FileName);
    }
}
