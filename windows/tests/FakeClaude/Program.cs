// Stands in for claude.exe in the wrapper tests: prints its arguments and environment.
// FAKE_EXIT sets its exit code.
using System.Text;

var o = new StringBuilder();
o.Append("ARGC=").Append(args.Length).Append('\n');
for (var i = 0; i < args.Length; i++) o.Append("ARG").Append(i).Append("=[").Append(args[i]).Append("]\n");
o.Append("ARGS=").Append(string.Join(' ', args)).Append('\n');
foreach (var v in new[] { "CLAUDE_CONFIG_DIR", "CLAUDE_PROFILE_ID", "CLAUDE_CODE_DISABLE_TERMINAL_TITLE",
                          "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" })
{
    o.Append(v).Append('=').Append(Environment.GetEnvironmentVariable(v) ?? "<unset>").Append('\n');
}
o.Append("CWD=").Append(Environment.CurrentDirectory).Append('\n');
using (var s = Console.OpenStandardOutput()) s.Write(new UTF8Encoding(false).GetBytes(o.ToString()));
return int.TryParse(Environment.GetEnvironmentVariable("FAKE_EXIT"), out var code) ? code : 0;
