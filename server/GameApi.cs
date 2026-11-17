using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SnakeClub.Core;

// JSON remains available for local compatibility. SQLite transactions are
// opt-in for a single-node deployment; TLS and operational setup remain external.
sealed class GameApi
{
    private readonly object gate = new();
    private readonly string path;
    private readonly byte[] secret;
    private GameData data;
    private readonly GameStore? store;
    private SqliteConnection? activeConnection;
    private SqliteTransaction? activeTransaction;
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan China = TimeSpan.FromHours(8);
    private static readonly HttpClient Http = new();

    private GameApi(string root)
    {
        path = Environment.GetEnvironmentVariable("SNAKE_DATA_FILE") ?? Path.Combine(root, "data", "v0.0.3.json");
        string? key = Environment.GetEnvironmentVariable("SNAKE_SESSION_KEY");
        string? database = Environment.GetEnvironmentVariable("SNAKE_SQLITE_FILE");
        bool production = string.Equals(Environment.GetEnvironmentVariable("SNAKE_ENV"), "production", StringComparison.OrdinalIgnoreCase);
        if (production && (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(key) || key.Length < 32))
            throw new InvalidOperationException("Production requires SNAKE_SQLITE_FILE and a stable SNAKE_SESSION_KEY of at least 32 characters.");
        if (production && (Environment.GetEnvironmentVariable("SNAKE_DEV_AUTH") == "1" || Environment.GetEnvironmentVariable("SNAKE_DEV_ADS") == "1"))
            throw new InvalidOperationException("Development authentication and ad simulation cannot be enabled in production.");
        secret = string.IsNullOrEmpty(key) ? RandomNumberGenerator.GetBytes(32) : SHA256.HashData(Encoding.UTF8.GetBytes(key));
        store = string.IsNullOrWhiteSpace(database) ? null : new GameStore(database, path, Json);
        data = store == null && File.Exists(path) ? JsonSerializer.Deserialize<GameData>(File.ReadAllText(path), Json) ?? new GameData() : new GameData();
    }

    public static void Map(WebApplication app, string root)
    {
        var service = new GameApi(root);
        app.MapPost("/api/v3/auth/dev", (HttpContext context, AuthRequest request) => service.DevLogin(context, request));
        app.MapPost("/api/v3/auth/douyin", (AuthRequest request) => service.DouyinLogin(request));
        app.MapGet("/api/v3/bootstrap", (HttpContext context) => service.WithPlayer(context, (profile, now) =>
            Results.Json(new { ok = true, profile, date = Day(now), seed = Seed(Day(now)), serverTime = now.ToUnixTimeMilliseconds(), adEnabled = Environment.GetEnvironmentVariable("SNAKE_DEV_ADS") == "1" })));
        app.MapPost("/api/v3/talent/allocate", (HttpContext context, IndexRequest request) => service.WithPlayer(context, (p, now) =>
            Progression.Allocate(p, request.index) ? Results.Json(new { ok = true, profile = p }) : Fail("天赋点不足或该天赋已满级")));
        app.MapPost("/api/v3/talent/recommend", (HttpContext context) => service.WithPlayer(context, (p, now) =>
            Progression.AllocateRecommended(p) > 0 ? Results.Json(new { ok = true, profile = p }) : Fail("没有可分配的天赋点")));
        app.MapPost("/api/v3/talent/reset", (HttpContext context) => service.WithPlayer(context, (p, now) => { p.talents = new int[9]; return Results.Json(new { ok = true, profile = p }); }));
        app.MapPost("/api/v3/skin/buy", (HttpContext context, IndexRequest request) => service.WithPlayer(context, (p, now) => service.BuySkin(p, request.index)));
        app.MapPost("/api/v3/skin/equip", (HttpContext context, IndexRequest request) => service.WithPlayer(context, (p, now) => EquipSkin(p, request.index)));
        app.MapPost("/api/v3/expedition/start", (HttpContext context, IndexRequest request) => service.WithPlayer(context, (p, now) => StartExpedition(p, request.index, now)));
        app.MapPost("/api/v3/expedition/claim", (HttpContext context, IdRequest request) => service.WithPlayer(context, (p, now) => service.ClaimExpedition(p, request.id, now)));
        app.MapPost("/api/v3/match/start", (HttpContext context) => service.WithPlayer(context, (p, now) => service.StartRun(p, now, "pve")));
        app.MapPost("/api/v3/match/finish", (HttpContext context, MatchRequest request) => service.WithPlayer(context, (p, now) => service.FinishPve(p, request, now)));
        app.MapPost("/api/v3/challenge/start", (HttpContext context) =>
        {
            string? rules = RequestedDailyRules(context);
            return rules == null ? Fail("挑战规则不存在") : service.WithPlayer(context, (p, now) => service.StartRun(p, now, "daily", rules));
        });
        app.MapPost("/api/v3/challenge/submit", (HttpContext context, ChallengeRequest request) => service.WithPlayer(context, (p, now) => service.SubmitChallenge(p, request, now)));
        app.MapPost("/api/v3/challenge/share", (HttpContext context, IdRequest request) => service.WithPlayer(context, (p, now) => service.CreateFriendChallenge(p, request.id, now)));
        app.MapGet("/api/v3/challenge/shared", (HttpContext context) => service.WithPlayer(context, (p, now) => service.ResolveFriendChallenge(context.Request.Query["token"].ToString(), now)));
        app.MapGet("/api/v3/challenge/leaderboard", (HttpContext context) =>
        {
            string? rules = RequestedDailyRules(context);
            return rules == null ? Fail("挑战规则不存在") : service.WithPlayer(context, (p, now) => service.Leaderboard(p, Day(now), rules));
        });
        app.MapPost("/api/v3/ad/prepare", (HttpContext context, AdRequest request) => service.WithPlayer(context, (p, now) => service.PrepareAd(p, request, now)));
        app.MapPost("/api/v3/ad/complete", (HttpContext context, IdRequest request) => service.WithPlayer(context, (p, now) => service.CompleteAd(p, request.id, now)));
    }

    private IResult DevLogin(HttpContext context, AuthRequest request)
    {
        if (Environment.GetEnvironmentVariable("SNAKE_DEV_AUTH") != "1" || context.Connection.RemoteIpAddress == null || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress)) return Fail("本地开发登录未启用", 403);
        if (string.IsNullOrWhiteSpace(request.deviceId) || request.deviceId.Length > 100) return Fail("设备标识无效");
        return Login("dev:" + request.deviceId);
    }

    private async Task<IResult> DouyinLogin(AuthRequest request)
    {
        string? appId = Environment.GetEnvironmentVariable("SNAKE_DOUYIN_APPID");
        string? appSecret = Environment.GetEnvironmentVariable("SNAKE_DOUYIN_SECRET");
        var result = await new DouyinSessionExchange(Http).Exchange(appId ?? "", appSecret ?? "", request.code, request.anonymousCode);
        if (result.Status != 200) return Fail(result.Error, result.Status);
        string identity = "douyin:" + appId + ":" + result.OpenId;
        string? anonymousIdentity = string.IsNullOrEmpty(result.AnonymousId) ? null : "douyin:" + appId + ":" + result.AnonymousId;
        return Login(identity, anonymousIdentity);
    }

    private IResult Login(string identity, string? fallbackIdentity = null)
    {
        string identityId = IdentityId(identity);
        string? fallbackId = string.IsNullOrEmpty(fallbackIdentity) ? null : IdentityId(fallbackIdentity);
        var now = DateTimeOffset.UtcNow;
        return Transact(null, new[] { identityId, fallbackId }, now, () =>
        {
            string id = data.identityLinks.TryGetValue(identityId, out var linked) ? linked : identityId;
            if (!data.players.ContainsKey(id) && fallbackId != null)
            {
                string fallback = fallbackId;
                if (data.identityLinks.TryGetValue(fallback, out var fallbackLinked)) fallback = fallbackLinked;
                if (data.players.ContainsKey(fallback)) id = fallback;
            }
            data.identityLinks[identityId] = id;
            if (!data.players.TryGetValue(id, out var profile))
            {
                profile = new PlayerProgress { playerId = id, displayName = "玩家" + id[..4] };
                data.players[id] = profile;
            }
            ResetDay(profile, now);
            long expiry = now.AddDays(7).ToUnixTimeSeconds();
            return Results.Json(new { ok = true, session = Sign(id + ":" + expiry), profile, serverTime = now.ToUnixTimeMilliseconds() });
        });
    }

    private static string IdentityId(string identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..24];

    private IResult WithPlayer(HttpContext context, Func<PlayerProgress, DateTimeOffset, IResult> action)
    {
        string header = context.Request.Headers.Authorization.ToString();
        string? payload = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? Verify(header[7..]) : null;
        var parts = payload?.Split(':');
        if (parts == null || parts.Length != 2 || !long.TryParse(parts[1], out var expiry) || expiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return Fail("登录已过期，请重新登录", 401);
        var now = DateTimeOffset.UtcNow;
        return Transact(parts[0], null, now, () =>
        {
            if (!data.players.TryGetValue(parts[0], out var profile)) return Fail("账号不存在", 401);
            if (profile.completedMatches == 0 && profile.tutorialComplete) profile.completedMatches = Math.Max(1, profile.dailyMatches);
            ResetDay(profile, now);
            return action(profile, now);
        });
    }

    private IResult Transact(string? playerId, string?[]? identities, DateTimeOffset now, Func<IResult> action)
    {
        lock (gate)
        {
            if (store == null)
            {
                var result = action();
                Save();
                return result;
            }
            using var connection = store.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            try
            {
                activeConnection = connection; activeTransaction = transaction;
                data = playerId == null ? store.LoadLogin(connection, transaction, identities ?? Array.Empty<string?>())
                    : store.LoadPlayerState(connection, transaction, playerId, Day(now), now.ToUnixTimeMilliseconds());
                var result = action();
                store.Save(connection, transaction, data);
                transaction.Commit();
                return result;
            }
            finally { activeConnection = null; activeTransaction = null; }
        }
    }

    private IResult BuySkin(PlayerProgress p, int index)
    {
        if (index <= 0 || index >= SkinCatalog.OwnedSkinCount) return Fail("皮肤不存在");
        if (p.ownedSkins.Contains(index)) return Results.Json(new { ok = true, profile = p });
        if (p.coins < Progression.SkinPrices[index]) return Fail("金币不足");
        p.coins -= Progression.SkinPrices[index]; p.ownedSkins.Add(index);
        return Results.Json(new { ok = true, profile = p });
    }

    private static IResult EquipSkin(PlayerProgress p, int index)
    {
        if (!p.ownedSkins.Contains(index)) return Fail("尚未拥有这款皮肤");
        p.selectedSkin = index; return Results.Json(new { ok = true, profile = p });
    }

    private static IResult StartExpedition(PlayerProgress p, int cave, DateTimeOffset now)
    {
        if (cave < 0 || cave >= Progression.CaveMinutes.Length) return Fail("洞穴不存在");
        if (p.expedition != null && !p.expedition.claimed) return Fail("当前探洞尚未领取");
        p.expedition = new ExpeditionState { id = Guid.NewGuid().ToString("N"), cave = cave, durationMinutes = Progression.CaveMinutes[cave], startedAt = now.ToUnixTimeMilliseconds(), bonusPercent = Progression.CaveBonusPercent(p) };
        return Results.Json(new { ok = true, profile = p, serverTime = now.ToUnixTimeMilliseconds() });
    }

    private IResult ClaimExpedition(PlayerProgress p, string id, DateTimeOffset now)
    {
        var expedition = p.expedition;
        if (expedition == null || expedition.id != id) return Fail("探洞记录不存在");
        if (expedition.claimed) return Results.Json(new { ok = true, profile = p, coins = expedition.coins, baseCoins = data.rewards.TryGetValue(id, out var prior) ? prior.baseCoins : 0, experience = expedition.experience, fragments = expedition.fragments });
        long elapsed = Math.Max(0, now.ToUnixTimeMilliseconds() - expedition.startedAt);
        int minutes = (int)Math.Min(expedition.durationMinutes, elapsed / 600000 * 10);
        expedition.endedAt = now.ToUnixTimeMilliseconds(); expedition.claimed = true;
        expedition.coins = Progression.ExpeditionCoins(minutes, expedition.bonusPercent);
        expedition.experience = Math.Min(20 - p.dailyExpeditionXp, Progression.ExpeditionExperience(minutes));
        if (minutes >= expedition.durationMinutes)
        {
            if (p.caveFragments == null || p.caveFragments.Length != 3) p.caveFragments = new int[3];
            expedition.fragments = 1;
            p.caveFragments[expedition.cave]++;
        }
        p.dailyExpeditionXp += expedition.experience; p.experience += expedition.experience; p.coins += expedition.coins;
        data.rewards[id] = new RewardSource { playerId = p.playerId, kind = "cave", baseCoins = minutes / 2 };
        return Results.Json(new { ok = true, profile = p, coins = expedition.coins, baseCoins = minutes / 2, experience = expedition.experience, fragments = expedition.fragments });
    }

    // Old clients did not send a rules version; continue issuing and ranking
    // daily-1 runs until the old client compatibility window is closed.
    private static string? RequestedDailyRules(HttpContext context)
    {
        string requested = context.Request.Query["rules"].ToString();
        if (string.IsNullOrEmpty(requested)) return DailyMatch.LegacyRulesVersion;
        return DailyMatch.SupportsRules(requested) ? requested : null;
    }

    private IResult StartRun(PlayerProgress p, DateTimeOffset now, string mode, string? requestedRules = null)
    {
        string date = Day(now);
        string rules = requestedRules ?? DailyMatch.RulesVersion;
        var run = new RunRecord { id = Guid.NewGuid().ToString("N"), playerId = p.playerId, mode = mode, date = date, rules = rules, seed = Seed(date, rules), startedAt = now.ToUnixTimeMilliseconds(), expiresAt = EndOfDay(now).AddMinutes(5).ToUnixTimeMilliseconds() };
        if (mode == "pve" && p.trialSkin > 0)
        {
            bool reserved = !string.IsNullOrEmpty(p.trialRunId) && data.runs.TryGetValue(p.trialRunId, out var trialRun) && !trialRun.completed && now.ToUnixTimeMilliseconds() - trialRun.startedAt < 190000;
            if (!reserved) { run.trialSkin = p.trialSkin; p.trialRunId = run.id; }
        }
        data.runs[run.id] = run;
        return Results.Json(new { ok = true, runId = run.id, token = RunToken(run), date, seed = run.seed, rules = run.rules, trialSkin = run.trialSkin, serverTime = run.startedAt, expiresAt = run.expiresAt });
    }

    private IResult FinishPve(PlayerProgress p, MatchRequest request, DateTimeOffset now)
    {
        var run = FindRun(p, request.runId, request.token, "pve", now);
        if (run == null) return Fail("对局凭证无效或已过期");
        if (run.completed) return Results.Json(new { ok = true, profile = p, coins = run.coins, experience = run.experience, questCoins = run.questCoins, questExperience = run.questExperience, unlockedSkin = run.unlockedSkin });
        double elapsed = (now.ToUnixTimeMilliseconds() - run.startedAt) / 1000.0;
        if (request.seconds < 3 || request.seconds > 180 || request.seconds > elapsed + 2) return Fail("对局时长无效");
        if (request.actions < 1) return Fail("未检测到操作，本局不发放账号奖励");
        run.completed = true; run.coins = p.dailyRewardedMatches < 20 ? 35 : 0;
        if (run.trialSkin > 0 && p.trialRunId == run.id) { p.trialSkin = -1; p.trialRunId = ""; }
        if (run.coins > 0) p.dailyRewardedMatches++;
        if (p.dailyMatches < 3) { p.dailyMatches++; run.experience = 20; p.experience += 20; }
        p.coins += run.coins; RecordCompletion(p, run);
        if (p.dailyMatches >= 2 && !p.dailyQuestClaimed)
        {
            p.dailyQuestClaimed = true; run.questExperience = 20; run.questCoins = 40 * (100 + Progression.TaskBonusPercent(p)) / 100;
            p.experience += run.questExperience; p.coins += run.questCoins;
        }
        if (run.coins > 0) data.rewards[run.id] = new RewardSource { playerId = p.playerId, kind = "match", baseCoins = run.coins };
        return Results.Json(new { ok = true, profile = p, coins = run.coins, experience = run.experience, questCoins = run.questCoins, questExperience = run.questExperience, unlockedSkin = run.unlockedSkin });
    }

    private IResult CreateFriendChallenge(PlayerProgress p, string? id, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(id) || !data.runs.TryGetValue(id, out var run) || run.playerId != p.playerId ||
            run.mode != "daily" || !run.completed || run.date != Day(now) || run.rules != DailyMatch.RulesVersion ||
            run.score < 0 || run.score > DailyMatch.MaxScoreFor(run.rules)) return Fail("没有可分享的已验证今日成绩");
        string token = FriendChallengeToken(run);
        return Results.Json(new { ok = true, shareToken = token, date = run.date, rules = run.rules, score = run.score });
    }

    private string FriendChallengeToken(RunRecord run) => Sign("friend-v1:" + run.date + ":" + run.rules + ":" + run.score.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private IResult ResolveFriendChallenge(string token, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 256) return Fail("好友挑战无效或已过期");
        string? payload = Verify(token);
        var parts = payload?.Split(':');
        if (parts == null || parts.Length != 4 || parts[0] != "friend-v1" || parts[1] != Day(now) ||
            parts[2] != DailyMatch.RulesVersion || !int.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int score) || score < 0 || score > DailyMatch.MaxScoreFor(parts[2]))
            return Fail("好友挑战无效或已过期");
        return Results.Json(new { ok = true, date = parts[1], rules = parts[2], score });
    }

    private IResult SubmitChallenge(PlayerProgress p, ChallengeRequest request, DateTimeOffset now)
    {
        var run = FindRun(p, request.runId, request.token, "daily", now);
        if (run == null) return Fail("挑战凭证无效或已过期");
        if (run.completed) return Results.Json(new { ok = true, profile = p, score = run.score, verified = true, coins = run.coins, experience = run.experience, questCoins = run.questCoins, questExperience = run.questExperience, unlockedSkin = run.unlockedSkin, shareToken = run.date == Day(now) && run.rules == DailyMatch.RulesVersion ? FriendChallengeToken(run) : "" });
        if (!DailyMatch.SupportsRules(run.rules)) return Fail("挑战规则已更新，请重新开始");
        if (request.actions < 1) return Fail("未检测到操作，本局不发放账号奖励");
        byte[] recording;
        try { recording = Convert.FromBase64String(request.inputs ?? ""); } catch (FormatException) { return Fail("回放格式无效"); }
        if (recording.Length == 0 || recording.Length > DailyMatch.MaxTicks * 2 || recording.Length % 2 != 0) return Fail("回放长度无效");
        var match = new DailyMatch(run.seed, run.rules);
        for (int i = 0; i < recording.Length; i += 2)
        {
            if (match.Finished || recording[i + 1] > 1) return Fail("回放内容无效");
            match.Step(recording[i], recording[i + 1] == 1);
        }
        if (!match.Finished || match.Score != request.score || match.Score < 0 || match.Score > DailyMatch.MaxScoreFor(run.rules)) return Fail("成绩与回放不一致");
        if (now.ToUnixTimeMilliseconds() - run.startedAt + 2000 < match.State.tick * 50L) return Fail("挑战时长无效");
        run.completed = true; run.score = match.Score; run.finishedAt = now.ToUnixTimeMilliseconds(); run.coins = p.dailyRewardedMatches < 20 ? 35 : 0;
        if (run.coins > 0) p.dailyRewardedMatches++;
        p.coins += run.coins; RecordCompletion(p, run);
        if (p.dailyMatches < 3) { p.dailyMatches++; run.experience = 20; p.experience += 20; }
        if (p.dailyMatches >= 2 && !p.dailyQuestClaimed)
        {
            p.dailyQuestClaimed = true; run.questExperience = 20; run.questCoins = 40 * (100 + Progression.TaskBonusPercent(p)) / 100;
            p.experience += run.questExperience; p.coins += run.questCoins;
        }
        if (run.coins > 0) data.rewards[run.id] = new RewardSource { playerId = p.playerId, kind = "match", baseCoins = run.coins };
        return Results.Json(new { ok = true, profile = p, score = run.score, verified = true, coins = run.coins, experience = run.experience, questCoins = run.questCoins, questExperience = run.questExperience, unlockedSkin = run.unlockedSkin, shareToken = run.date == Day(now) && run.rules == DailyMatch.RulesVersion ? FriendChallengeToken(run) : "" });
    }

    private static void RecordCompletion(PlayerProgress p, RunRecord run)
    {
        p.tutorialComplete = true;
        if (p.completedMatches < int.MaxValue) p.completedMatches++;
        // Inventory is the permanent, idempotent record of this early reward.
        // Existing owners retain their purchase and never receive a duplicate.
        if (p.completedMatches >= 2 && !p.ownedSkins.Contains(1))
        {
            p.ownedSkins.Add(1);
            run.unlockedSkin = 1;
        }
    }

    private IResult Leaderboard(PlayerProgress p, string day, string rules)
    {
        if (store != null && activeConnection != null && activeTransaction != null)
            return store.Leaderboard(activeConnection, activeTransaction, p.playerId, day, rules);
        var best = data.runs.Values.Where(r => r.mode == "daily" && r.completed && r.date == day && r.rules == rules)
            .GroupBy(r => r.playerId).Select(g => g.OrderByDescending(r => r.score).ThenBy(r => r.finishedAt).First())
            .OrderByDescending(r => r.score).ThenBy(r => r.finishedAt).ThenBy(r => r.playerId, StringComparer.Ordinal).ToList();
        var rows = best.Take(100).Select((r, i) => new { rank = i + 1, playerId = r.playerId, name = data.players.TryGetValue(r.playerId, out var player) ? player.displayName : "玩家", score = r.score }).ToList();
        int position = best.FindIndex(r => r.playerId == p.playerId);
        int gap = position > 0 ? best[position - 1].score - best[position].score : 0;
        return Results.Json(new { ok = true, date = day, rows, rank = position + 1, total = best.Count, gap });
    }

    private IResult PrepareAd(PlayerProgress p, AdRequest request, DateTimeOffset now)
    {
        if (!p.tutorialComplete) return Fail("完成首局后可选择广告奖励");
        RewardSource? source = null;
        bool trial = request.kind == "trial";
        if (trial)
        {
            if (request.index <= 0 || request.index >= SkinCatalog.OwnedSkinCount || p.ownedSkins.Contains(request.index) || p.dailyTrialUsed) return Fail("今天暂不可试穿该皮肤");
        }
        else
        {
            if (!data.rewards.TryGetValue(request.sourceId ?? "", out source) || source.playerId != p.playerId || source.adClaimed) return Fail("当前奖励不可再次领取");
            if (source.kind != request.kind) return Fail("奖励类型不匹配");
        }
        string day = Day(now);
        var today = data.ads.Values.Where(a => a.playerId == p.playerId && a.day == day && a.completed).ToList();
        if (today.Count >= 5 || today.Any(a => now.ToUnixTimeMilliseconds() - a.completedAt < 180000)) return Fail("广告奖励暂时不可用");
        var ad = new AdRecord { id = Guid.NewGuid().ToString("N"), playerId = p.playerId, sourceId = request.sourceId ?? "", kind = request.kind ?? "", skin = trial ? request.index : -1, day = day, createdAt = now.ToUnixTimeMilliseconds() };
        data.ads[ad.id] = ad;
        return Results.Json(new { ok = true, id = ad.id, coins = source?.baseCoins ?? 0 });
    }

    private IResult CompleteAd(PlayerProgress p, string id, DateTimeOffset now)
    {
        // Until a verified platform callback is integrated this is explicitly
        // disabled, except for opt-in local development simulations.
        if (Environment.GetEnvironmentVariable("SNAKE_DEV_ADS") != "1") return Fail("广告奖励尚未接入", 503);
        if (!data.ads.TryGetValue(id, out var ad) || ad.playerId != p.playerId) return Fail("奖励记录不存在");
        bool trial = ad.kind == "trial";
        RewardSource? source = null;
        if (!trial && !data.rewards.TryGetValue(ad.sourceId, out source)) return Fail("奖励记录不存在");
        if (ad.completed) return Results.Json(new { ok = true, profile = p, coins = source?.baseCoins ?? 0 });
        if ((trial ? p.dailyTrialUsed : source!.adClaimed) || ad.day != Day(now) || now.ToUnixTimeMilliseconds() - ad.createdAt > 600000) return Fail("奖励已领取或已过期");
        var completed = data.ads.Values.Where(a => a.playerId == p.playerId && a.day == ad.day && a.completed).ToList();
        if (completed.Count >= 5 || completed.Any(a => now.ToUnixTimeMilliseconds() - a.completedAt < 180000)) return Fail("广告奖励暂时不可用");
        ad.completed = true; ad.completedAt = now.ToUnixTimeMilliseconds();
        if (trial) { p.dailyTrialUsed = true; p.trialSkin = ad.skin; p.trialRunId = ""; }
        else { source!.adClaimed = true; p.coins += source.baseCoins; }
        return Results.Json(new { ok = true, profile = p, coins = source?.baseCoins ?? 0 });
    }

    private RunRecord? FindRun(PlayerProgress p, string? id, string? token, string mode, DateTimeOffset now)
    {
        if (id == null || token == null || !data.runs.TryGetValue(id, out var run) || run.playerId != p.playerId || run.mode != mode || now.ToUnixTimeMilliseconds() > run.expiresAt) return null;
        return Verify(token) == RunPayload(run) ? run : null;
    }
    private string RunToken(RunRecord run) => Sign(RunPayload(run));
    private static string RunPayload(RunRecord run) => run.id + ":" + run.playerId + ":" + run.mode + ":" + run.date + ":" + run.rules + ":" + run.seed + ":" + run.expiresAt;
    private string Sign(string value)
    {
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string signature = Convert.ToBase64String(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(payload))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return payload + "." + signature;
    }
    private string? Verify(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return null;
        string expected = Convert.ToBase64String(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(parts[0]))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1]))) return null;
        try { string encoded = parts[0].Replace('-', '+').Replace('_', '/'); return Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))); }
        catch (FormatException) { return null; }
    }
    private void Save()
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, Json));
        File.Move(temporary, path, true);
    }
    private static IResult Fail(string message, int status = 400) => Results.Json(new { ok = false, error = message }, statusCode: status);
    private static string Day(DateTimeOffset now) => now.ToOffset(China).ToString("yyyy-MM-dd");
    private static DateTimeOffset EndOfDay(DateTimeOffset now) { var local = now.ToOffset(China); return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, China).AddDays(1); }
    private static int Seed(string date, string rules = DailyMatch.RulesVersion) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(rules + ":" + date)), 0);
    private static void ResetDay(PlayerProgress p, DateTimeOffset now)
    {
        string date = Day(now);
        if (p.dailyKey == date) return;
        p.dailyKey = date; p.dailyMatches = 0; p.dailyRewardedMatches = 0; p.dailyExpeditionXp = 0; p.dailyQuestClaimed = false; p.dailyTrialUsed = false; p.trialSkin = -1; p.trialRunId = "";
    }
}

sealed class GameData
{
    public Dictionary<string, PlayerProgress> players = new();
    public Dictionary<string, string> identityLinks = new();
    public Dictionary<string, RunRecord> runs = new();
    public Dictionary<string, RewardSource> rewards = new();
    public Dictionary<string, AdRecord> ads = new();
}
sealed class RunRecord
{
    // Records written before this field was persisted inherited daily-2 in the
    // previous server. New runs always assign their requested version explicitly.
    public string id = "", playerId = "", mode = "", date = "", rules = DailyMatch.PreviousRulesVersion;
    public int seed, score, coins, experience, questCoins, questExperience, trialSkin = -1, unlockedSkin = -1;
    public long startedAt, finishedAt, expiresAt;
    public bool completed;
}
sealed class RewardSource { public string playerId = "", kind = ""; public int baseCoins; public bool adClaimed; }
sealed class AdRecord { public string id = "", playerId = "", sourceId = "", kind = "", day = ""; public int skin = -1; public long createdAt, completedAt; public bool completed; }
sealed class AuthRequest { public string? code { get; set; } public string? anonymousCode { get; set; } public string? deviceId { get; set; } }
sealed class IndexRequest { public int index { get; set; } }
sealed class IdRequest { public string id { get; set; } = ""; }
sealed class MatchRequest { public string? runId { get; set; } public string? token { get; set; } public int seconds { get; set; } public int actions { get; set; } }
sealed class ChallengeRequest { public string? runId { get; set; } public string? token { get; set; } public int score { get; set; } public int actions { get; set; } public string? inputs { get; set; } }
sealed class AdRequest { public string? sourceId { get; set; } public string? kind { get; set; } public int index { get; set; } }
