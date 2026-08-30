namespace PersonalManagement.Desktop;

internal static class SchemaService
{
    public const string BaseTitle = "PersonalManagement";

    public static async Task<SchemaIds> EnsureAsync(NocoClient noco)
    {
        var baseId = await noco.EnsureBaseAsync(BaseTitle);

        var tasks = await noco.EnsureTableAsync(baseId, "tasks",
        [
            Col("Type", "SingleLineText"),
            Col("RewardLevel", "Number"),
            Col("RegisteredAt", "DateTime"),
            Col("DueAt", "DateTime"),
            Col("ReminderAt", "DateTime"),
            Col("Color", "SingleLineText")
        ]);
        await noco.EnsureColumnAsync(tasks, Col("Color", "SingleLineText"));
        await noco.EnsureColumnAsync(tasks, Col("BlockPattern", "SingleLineText"));
        await noco.EnsureColumnAsync(tasks, Col("BlockPatternColor", "SingleLineText"));
        await noco.EnsureColumnAsync(tasks, Col("BlockStyleJson", "LongText"));
        await noco.EnsureColumnAsync(tasks, Col("Thumb", "Attachment"));
        await noco.EnsureColumnAsync(tasks, Col("Original", "Attachment"));
        await noco.EnsureColumnAsync(tasks, Col("CropJson", "LongText"));
        await noco.EnsureColumnAsync(tasks, Col("RewardMinutes", "Number"));
        await noco.EnsureColumnAsync(tasks, Col("AllowOverflow", "Checkbox"));
        await noco.EnsureColumnAsync(tasks, Col("OverflowSeconds", "Number"));
        await noco.EnsureColumnAsync(tasks, Col("Archived", "Checkbox"));
        await noco.EnsureColumnAsync(tasks, Col("IsDirectProductivity", "Checkbox"));
        var completions = await noco.EnsureTableAsync(baseId, "completions",
        [
            Col("CompletedOn", "Date")
        ]);
        var sessions = await noco.EnsureTableAsync(baseId, "sessions",
        [
            Col("StartedAt", "DateTime"),
            Col("EndedAt", "DateTime")
        ]);
        await noco.EnsureColumnAsync(sessions, Col("Outcome", "SingleLineText"));
        await noco.EnsureColumnAsync(sessions, Col("PausedSeconds", "Number"));
        await noco.EnsureColumnAsync(sessions, Col("PauseJson", "LongText"));
        var scheduleNotes = await noco.EnsureTableAsync(baseId, "schedule_notes",
        [
            Col("At", "DateTime"),
            Col("DayColumnPercent", "Number"),
            Col("Body", "LongText"),
            Col("CreatedAt", "DateTime")
        ]);
        await noco.EnsureColumnAsync(scheduleNotes, Col("At", "DateTime"));
        await noco.EnsureColumnAsync(scheduleNotes, Col("DayColumnPercent", "Number"));
        await noco.EnsureColumnAsync(scheduleNotes, Col("Body", "LongText"));
        await noco.EnsureColumnAsync(scheduleNotes, Col("CreatedAt", "DateTime"));
        var rewards = await noco.EnsureTableAsync(baseId, "reward_pool",
        [
            Col("Kind", "SingleLineText"),
            Col("QuotaAmount", "Number"),
            Col("Weight", "Number")
        ]);
        await noco.EnsureColumnAsync(rewards, Col("Thumb", "Attachment"));
        await noco.EnsureColumnAsync(rewards, Col("Original", "Attachment"));
        await noco.EnsureColumnAsync(rewards, Col("CropJson", "LongText"));
        await noco.EnsureColumnAsync(rewards, Col("Archived", "Checkbox"));
        await noco.EnsureColumnAsync(rewards, Col("Probability", "Number"));
        await noco.EnsureColumnAsync(rewards, Col("IsBase", "Checkbox"));
        var wishlist = await noco.EnsureTableAsync(baseId, "wishlist",
        [
            Col("Cost", "Number")
        ]);
        await noco.EnsureColumnAsync(wishlist, Col("Thumb", "Attachment"));
        await noco.EnsureColumnAsync(wishlist, Col("Original", "Attachment"));
        await noco.EnsureColumnAsync(wishlist, Col("CropJson", "LongText"));
        await noco.EnsureColumnAsync(wishlist, Col("Archived", "Checkbox"));
        var state = await noco.EnsureTableAsync(baseId, "app_state",
        [
            Col("DrawTickets", "Number"),
            Col("WishlistQuota", "Number"),
            Col("PrivatePin", "SingleLineText")
        ]);
        var favorites = await noco.EnsureTableAsync(baseId, "favorites",
        [
            Col("Kind", "SingleLineText"),
            Col("Source", "LongText"),
            Col("Tags", "SingleLineText"),
            Col("IsPrivate", "Checkbox"),
            Col("File", "Attachment"),
            Col("Thumb", "Attachment")
        ]);
        await noco.EnsureColumnAsync(favorites, Col("Original", "Attachment"));
        await noco.EnsureColumnAsync(favorites, Col("CropJson", "LongText"));
        await noco.EnsureColumnAsync(state, Col("PrivatePin", "SingleLineText"));
        await noco.EnsureColumnAsync(state, Col("RewardScheme", "SingleLineText"));

        await noco.EnsureLinkColumnAsync(tasks, completions, "Task");
        await noco.EnsureLinkColumnAsync(tasks, sessions, "Task");

        var stateRows = await noco.ListRecordsAsync(state);
        if (stateRows.Count == 0)
        {
            await noco.CreateRecordAsync(state, new Dictionary<string, object?>
            {
                ["Title"] = "main",
                ["DrawTickets"] = 0,
                ["WishlistQuota"] = 0,
                ["RewardScheme"] = "prob-v1"
            });
        }
        else
        {
            var row = stateRows[0];
            var scheme = NocoClient.ReadString(row, "RewardScheme");
            if (scheme != "prob-v1")
            {
                var rewardRows = await noco.ListRecordsAsync(rewards);
                foreach (var r in rewardRows)
                {
                    var rid = NocoClient.ReadId(r);
                    if (rid is null) continue;
                    await noco.PatchRecordAsync(rewards, new Dictionary<string, object?>
                    {
                        ["Id"] = rid,
                        ["Probability"] = 0,
                        ["IsBase"] = false
                    });
                }
                var sid = NocoClient.ReadId(row);
                if (sid is not null)
                {
                    await noco.PatchRecordAsync(state, new Dictionary<string, object?>
                    {
                        ["Id"] = sid,
                        ["RewardScheme"] = "prob-v1"
                    });
                }
            }
        }

        var pool = await noco.ListRecordsAsync(rewards);
        if (pool.Count == 0)
        {
            await noco.CreateRecordAsync(rewards, Prize("贴纸一张", "item", 0, 0, true));
            await noco.CreateRecordAsync(rewards, Prize("愿望单额度 +1", "quota", 1, 0, false));
            await noco.CreateRecordAsync(rewards, Prize("愿望单额度 +3", "quota", 3, 0, false));
            await noco.CreateRecordAsync(rewards, Prize("奖券 +1", "ticket", 1, 0, false));
        }

        var weightProfile = await noco.EnsureTableAsync(baseId, "weight_profile",
        [
            Col("HeightCm", "Number"),
            Col("AgeYears", "Number"),
            Col("Sex", "SingleLineText"),
            Col("Activity", "SingleLineText")
        ]);
        var weightEntries = await noco.EnsureTableAsync(baseId, "weight_entries",
        [
            Col("Date", "SingleLineText"),
            Col("WeightKg", "Number")
        ]);

        return new SchemaIds
        {
            BaseId = baseId,
            Tasks = tasks,
            Completions = completions,
            Sessions = sessions,
            ScheduleNotes = scheduleNotes,
            Rewards = rewards,
            Wishlist = wishlist,
            State = state,
            Favorites = favorites,
            WeightProfile = weightProfile,
            WeightEntries = weightEntries
        };
    }

    private static Dictionary<string, object?> Col(string title, string uidt) =>
        new() { ["title"] = title, ["uidt"] = uidt };

    private static Dictionary<string, object?> Prize(string title, string kind, int quota, double probability, bool isBase) =>
        new()
        {
            ["Title"] = title,
            ["Kind"] = kind,
            ["QuotaAmount"] = quota,
            ["Probability"] = probability,
            ["IsBase"] = isBase,
            ["Archived"] = false
        };
}
