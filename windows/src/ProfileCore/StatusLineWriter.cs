using System.Text.Json;

namespace ProfileCore;

/// Status line for added profiles. Written once at creation; the label reads the display name from
/// names. The built-in profile is refused: the app never writes to ~\.claude.
public sealed class StatusLineWriter(ConfigLocator locator)
{
    /// The path is quoted because home folders can have spaces (design "Status line").
    public string Command(Profile p) => $"\"{Path.Combine(locator.BinDir, "claude.exe")}\" --profile-label {p.Id}";

    public void Write(Profile p)
    {
        if (p.Builtin || p.ConfigDir == null) throw new ProfileException("The app never writes to ~\\.claude.");
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartObject("statusLine");
            w.WriteString("command", Command(p));
            w.WriteString("type", "command");
            w.WriteEndObject();
            w.WriteEndObject();
        }
        AtomicFile.Write(Path.Combine(p.ConfigDir, "settings.json"), ms.ToArray());
    }
}
