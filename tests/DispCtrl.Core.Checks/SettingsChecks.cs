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
    }
}
