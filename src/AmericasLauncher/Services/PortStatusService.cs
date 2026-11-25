// SPDX-License-Identifier: GPL-2.0-only
using System.Net.Http;
using System.Text.Json;

namespace AmericasLauncher.Services;

public sealed record PortMilestone(string Key, string Label, double Threshold, bool Reached);

public sealed record PortChangeItem(string Date, string Kind, string Text);

public sealed record PortStatusReport(
    string UpdatedAt,
    string LeaderName,
    string LeaderRepo,
    double LeaderScore,
    double Top3Average,
    string ReachedLabel,
    string StoryLabel,
    string RenderLabel,
    double Fps,
    string StabilityLabel,
    List<PortMilestone> Milestones,
    List<PortChangeItem> Changes);

public sealed class PortStatusService
{
    private static readonly string[] TrackerUrls =
    [
        "https://portwatch-vi.xyz/data/tracker.json",
        "https://raw.githubusercontent.com/4lwayz/portwatch-vi/main/data/tracker.json"
    ];

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AmericasLauncher/{LauncherUpdateService.CurrentVersionString}");
        return client;
    }

    public async Task<PortStatusReport> FetchAsync(CancellationToken ct = default)
    {
        string? json = null;
        Exception? lastError = null;
        foreach (var url in TrackerUrls)
        {
            try
            {
                json = await Http.GetStringAsync(url, ct);
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (json == null)
            throw lastError ?? new HttpRequestException("Failed to fetch Portwatch VI data.");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var updated = root.TryGetProperty("updated", out var updEl) ? updEl.GetString() ?? "" : "";

        // Score all non-nuked efforts using the same model as portwatch-vi/assets/js/scoring.js
        var scored = new List<(string Name, string Repo, double Score, int Stars, string Reached, string Story, string Render, double Fps, string Stability)>();

        if (root.TryGetProperty("efforts", out var effortsEl) && effortsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var eff in effortsEl.EnumerateArray())
            {
                if (eff.TryGetProperty("nuked", out var nukedEl) && nukedEl.ValueKind == JsonValueKind.True)
                    continue;

                var name = eff.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "Unknown" : "Unknown";
                var repo = eff.TryGetProperty("repo", out var rEl) && rEl.ValueKind == JsonValueKind.String
                    ? rEl.GetString() ?? "Private build"
                    : "Private build";
                var stars = eff.TryGetProperty("stats", out var statsEl) && statsEl.TryGetProperty("stars", out var stEl) && stEl.TryGetInt32(out var sVal)
                    ? sVal
                    : 0;

                var (score, reached, story, render, fps, stability) = ScoreEffort(eff);
                scored.Add((name, repo, score, stars, reached, story, render, fps, stability));
            }
        }

        scored.Sort((a, b) =>
        {
            var cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : b.Stars.CompareTo(a.Stars);
        });

        var leader = scored.FirstOrDefault();
        var top3 = scored.Count > 0
            ? Math.Round(scored.Take(3).Average(x => x.Score), 1)
            : 0;

        var milestones = new List<PortMilestone>
        {
            new("groundwork", "Groundwork", 0, leader.Score >= 0),
            new("loads", "Exe runs", 10, leader.Score >= 10),
            new("boots", "First frame", 20, leader.Score >= 20),
            new("menu", "Main menu", 30, leader.Score >= 30),
            new("world", "In the world", 40, leader.Score >= 40),
            new("playable", "Playable", 50, leader.Score >= 50),
            new("complete", "Complete", 100, leader.Score >= 100)
        };

        var changes = new List<PortChangeItem>();
        if (root.TryGetProperty("changes", out var changesEl) && changesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var ch in changesEl.EnumerateArray().Take(25))
            {
                var date = ch.TryGetProperty("date", out var dEl) ? dEl.GetString() ?? "" : "";
                var kind = ch.TryGetProperty("kind", out var kEl) ? kEl.GetString() ?? "update" : "update";
                var text = ch.TryGetProperty("text", out var tEl) ? tEl.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(text))
                    changes.Add(new PortChangeItem(date, kind.ToUpperInvariant(), text));
            }
        }

        return new PortStatusReport(
            UpdatedAt: updated,
            LeaderName: string.IsNullOrEmpty(leader.Name) ? "-" : leader.Name,
            LeaderRepo: string.IsNullOrEmpty(leader.Repo) ? "-" : leader.Repo,
            LeaderScore: leader.Score,
            Top3Average: top3,
            ReachedLabel: string.IsNullOrEmpty(leader.Reached) ? "-" : leader.Reached,
            StoryLabel: string.IsNullOrEmpty(leader.Story) ? "-" : leader.Story,
            RenderLabel: string.IsNullOrEmpty(leader.Render) ? "-" : leader.Render,
            Fps: leader.Fps,
            StabilityLabel: string.IsNullOrEmpty(leader.Stability) ? "-" : leader.Stability,
            Milestones: milestones,
            Changes: changes);
    }

    private static (double Score, string Reached, string Story, string Render, double Fps, string Stability) ScoreEffort(JsonElement eff)
    {
        var gw = 0.0;
        if (eff.TryGetProperty("groundwork", out var gwEl) && gwEl.ValueKind == JsonValueKind.Object)
        {
            gw += 0.20 * ReadClamp01(gwEl, "loader");
            gw += 0.28 * ReadClamp01(gwEl, "shaders");
            gw += 0.32 * ReadClamp01(gwEl, "graphics");
            gw += 0.20 * ReadClamp01(gwEl, "proven");
        }

        var hasGta = eff.TryGetProperty("gta", out var gtaEl) && gtaEl.ValueKind == JsonValueKind.Object;
        var ladder = new (string Key, string Label, double At)[]
        {
            ("loads", "Exe runs", 10),
            ("boots", "First frame", 20),
            ("menu", "Main menu", 30),
            ("world", "In the world", 40),
            ("playable", "Playable", 50)
        };

        var score = 10.0 * gw;
        var reached = "Groundwork";
        var prevTick = score;

        if (hasGta)
        {
            foreach (var step in ladder)
            {
                var v = ReadLadder(gtaEl, step.Key);
                if (v >= 1.0)
                {
                    score = step.At;
                    prevTick = score;
                    reached = step.Label;
                    continue;
                }
                if (v > 0.0)
                    score = Math.Max(score, prevTick + v * (step.At - prevTick));
                break;
            }
        }

        var storyVal = hasGta ? ReadClamp01(gtaEl, "story") : 0.0;
        var renderVal = hasGta ? ReadClamp01(gtaEl, "render") : 0.0;
        var fps = hasGta ? ReadDouble(gtaEl, "fps") : 0.0;
        var perfVal = Math.Clamp(fps / 30.0, 0.0, 1.0);
        var (stabVal, stabLabel) = hasGta ? ReadStability(gtaEl) : (0.0, "Unknown");

        if (reached == "Playable")
        {
            var quality = 0.35 * storyVal + 0.25 * renderVal + 0.20 * perfVal + 0.20 * stabVal;
            score = 50.0 + quality * 50.0;
            if (quality >= 1.0) reached = "Complete";
        }

        if (eff.TryGetProperty("manualScore", out var manEl) && manEl.TryGetDouble(out var manual))
            score = Math.Clamp(manual, 0.0, 100.0);

        return (
            Math.Round(score, 1),
            reached,
            StoryLabel(storyVal),
            RenderLabel(renderVal),
            fps,
            stabLabel);
    }

    private static double ReadDouble(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return 0.0;
        return el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v) ? v : 0.0;
    }

    private static double ReadClamp01(JsonElement obj, string prop) =>
        Math.Clamp(ReadDouble(obj, prop), 0.0, 1.0);

    private static double ReadLadder(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el)) return 0.0;
        return el.ValueKind switch
        {
            JsonValueKind.True => 1.0,
            JsonValueKind.False => 0.0,
            JsonValueKind.Number when el.TryGetDouble(out var v) => Math.Clamp(v, 0.0, 1.0),
            _ => 0.0
        };
    }

    private static (double Value, string Label) ReadStability(JsonElement gta)
    {
        if (!gta.TryGetProperty("stability", out var el)) return (0.0, "Unknown");
        if (el.ValueKind == JsonValueKind.String)
        {
            return (el.GetString() ?? "") switch
            {
                "constant" => (0.0, "Crashes constantly"),
                "often" => (0.25, "Crashes often"),
                "sometimes" => (0.55, "Crashes sometimes"),
                "rarely" => (0.8, "Rarely crashes"),
                "stable" => (1.0, "Stable"),
                _ => (0.0, "Unknown")
            };
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var num))
        {
            var v = Math.Clamp(num, 0.0, 1.0);
            var options = new (double Val, string Label)[]
            {
                (0.0, "Crashes constantly"),
                (0.25, "Crashes often"),
                (0.55, "Crashes sometimes"),
                (0.8, "Rarely crashes"),
                (1.0, "Stable")
            };
            var best = options.OrderBy(o => Math.Abs(o.Val - v)).First();
            return (v, best.Label);
        }

        return (0.0, "Unknown");
    }

    private static string StoryLabel(double v) => v switch
    {
        >= 1.0 => "100% completion",
        >= 0.85 => "Main story to the credits",
        >= 0.6 => "Half the story",
        >= 0.4 => "First act",
        >= 0.25 => "First missions",
        >= 0.1 => "Prologue cleared",
        _ => "Stuck in the opening"
    };

    private static string RenderLabel(double v) => v switch
    {
        >= 1.0 => "Matches the console",
        >= 0.75 => "Close to the console, minor glitches",
        >= 0.5 => "Lit, but major effects missing",
        >= 0.25 => "Shapes drawn, mostly unlit or black",
        _ => "Black or broken"
    };
}
