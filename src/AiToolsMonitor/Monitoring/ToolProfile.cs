namespace AiToolsMonitor.Monitoring;

/// <summary>One monitored CLI tool and the substrings that identify its processes.</summary>
public sealed record ToolProfile(string DisplayName, string[] MatchTerms)
{
    public bool Matches(string processNameLower, string commandLineLower)
    {
        foreach (var term in MatchTerms)
        {
            var t = term.ToLowerInvariant();
            if (processNameLower.Contains(t) || commandLineLower.Contains(t))
                return true;
        }
        return false;
    }

    public static readonly ToolProfile[] Defaults =
    [
        new("Claude Code", ["claude.exe", "claude", "anthropic-ai/claude-code"]),
        new("Hermes Agent", ["hermes.exe", "hermes-agent.exe", "hermes"]),
        new("Codex", ["codex.exe", "openai/codex"]),
        new("OpenCode", ["opencode.cmd", "opencode.exe", "opencode"]),
        new("Antigravity", ["agy.exe", "antigravity"]),
    ];
}
