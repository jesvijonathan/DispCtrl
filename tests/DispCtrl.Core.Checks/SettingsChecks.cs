using System.Text.Json.Nodes;
using DispCtrl.Core.Settings;

internal static class SettingsChecks
{
    // SettingsStore.MergeEdits is what lets the app, the engine and the CLI save
    // the same file without undoing each other. Pure JSON, no file touched.
    public static void Run(Action<bool, string> check)
    {
        JsonNode Json(string text) => JsonNode.Parse(text)!;

        var merged = SettingsStore.MergeEdits(Json("""{"a":1,"b":1}"""), Json("""{"a":2,"b":1}"""), Json("""{"a":1,"b":3}"""));
        check(JsonNode.DeepEquals(merged, Json("""{"a":2,"b":3}""")), "a save keeps another client's edit to a different field");

        merged = SettingsStore.MergeEdits(Json("""{"a":1}"""), Json("""{"a":1}"""), Json("""{"a":5,"c":2}"""));
        check(JsonNode.DeepEquals(merged, Json("""{"a":5,"c":2}""")), "a save with no local edits writes the file as it is on disk");

        merged = SettingsStore.MergeEdits(Json("""{"a":1}"""), Json("""{"a":2}"""), Json("""{"a":3}"""));
        check(JsonNode.DeepEquals(merged, Json("""{"a":2}""")), "when both changed one field, this client's edit wins");

        merged = SettingsStore.MergeEdits(Json("""{"g":{"x":1,"y":1}}"""), Json("""{"g":{"x":2,"y":1}}"""), Json("""{"g":{"x":1,"y":4}}"""));
        check(JsonNode.DeepEquals(merged, Json("""{"g":{"x":2,"y":4}}""")), "nested objects merge field by field");

        merged = SettingsStore.MergeEdits(Json("""{"a":1,"gone":1}"""), Json("""{"a":1}"""), Json("""{"a":1,"gone":1,"new":1}"""));
        check(JsonNode.DeepEquals(merged, Json("""{"a":1,"new":1}""")), "a field this client removed stays removed; one another client added stays");

        merged = SettingsStore.MergeEdits(Json("""{"l":[1,2]}"""), Json("""{"l":[1,2,3]}"""), Json("""{"l":[9]}"""));
        check(JsonNode.DeepEquals(merged, Json("""{"l":[1,2,3]}""")), "a list is replaced whole, never interleaved");

        var panel = new QuickPanelSettings();
        check(panel.IsCollapsed("unison") && !panel.IsCollapsed("simpleBrightness") && !panel.IsCollapsed("tiles") && panel.IsCollapsed("displays")
                && panel.IsCollapsed("display:DEL-A234-X") && panel.IsCollapsed("windows"),
            "a new quick panel opens with Brightness and Quick toggles unfolded, each display's block folded");
        panel.SetCollapsed("display:DEL-A234-X", false);
        check(!panel.IsCollapsed("display:DEL-A234-X") && panel.Expanded.Contains("display:DEL-A234-X") && !panel.Collapsed.Contains("display:DEL-A234-X"),
            "a display block somebody opens is remembered as opened");

        check(UnisonResume.SoftwareLevel(60) == 60 && UnisonResume.SoftwareLevel(0) == DispCtrl.Core.Color.NightLight.MinimumDim
                && UnisonResume.SoftwareLevel(140) == 100 && !new MonitorSettings().SoftwareDimming,
            "unison dims a display with no control of its own in software, never below the readable floor; dimming in software is off by default");

        check(DispCtrl.Core.Displays.GraphicsAdapter.InstanceFromPath(@"\\?\ROOT#DISPLAY#0000#{5b45201d-f2f2-4f3b-85bb-30ff1f953599}") == @"ROOT\DISPLAY\0000"
                && DispCtrl.Core.Displays.GraphicsAdapter.InstanceFromPath(@"\\?\PCI#VEN_1002&DEV_1638#4&1&0&0041#{5b45201d-f2f2-4f3b-85bb-30ff1f953599}") == @"PCI\VEN_1002&DEV_1638\4&1&0&0041"
                && !DispCtrl.Core.Displays.GraphicsAdapter.For("").Software,
            "an adapter's device path gives its instance, and an unknown adapter is not called software");

        // The request for support: never on a first run, ended by a star or a
        // donation, asked once more after a close, and never after a second.
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var ask = new SupportPrompt();
        for (int i = 0; i < SupportPrompt.OpensBefore; i++) ask.CountOpen(start);
        check(!ask.Due(start) && !ask.Due(start + SupportPrompt.Settle - TimeSpan.FromMinutes(1)) && ask.Due(start + SupportPrompt.Settle),
            "support is asked only after five openings over three days");
        var few = new SupportPrompt();
        few.CountOpen(start);
        check(!few.Due(start + TimeSpan.FromDays(30)), "a month with one opening does not ask");
        DateTimeOffset closed = start + TimeSpan.FromDays(4);
        ask.Decline(closed);
        check(!ask.Due(closed + TimeSpan.FromDays(1)) && ask.Due(closed + SupportPrompt.Again), "closed once, it asks again only after ninety days");
        ask.Decline(closed + SupportPrompt.Again);
        check(ask.Done && !ask.Due(closed + TimeSpan.FromDays(1000)), "closed twice, it never asks again");
        var given = new SupportPrompt();
        for (int i = 0; i < 10; i++) given.CountOpen(start);
        given.Acted();
        check(!given.Due(start + TimeSpan.FromDays(400)), "a star or a donation ends it");
        var reset = new DispCtrlSettings();
        reset.Global.Support.Acted();
        reset.Global.ResetToDefaults();
        check(reset.Global.Support.Done && reset.Global.Updates.CheckAutomatically, "Reset all keeps the support answer and puts the daily update check on");
    }
}
