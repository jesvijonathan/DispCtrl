using System.Reflection;
using System.Text.Json.Nodes;
using DispCtrl.Display.Devices;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    private static JsonObject ReportCommand(JsonObject args)
    {
        string version = typeof(ControlService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "unknown";
        ProblemReport report = ProblemReports.Build(Text(args, "what"), Text(args, "steps"), version);
        return new JsonObject
        {
            ["body"] = report.Text, ["url"] = report.Url.AbsoluteUri, ["paste"] = report.Paste,
            ["note"] = report.Paste is null ? "Review the report, then open the issue link. Submit it on GitHub."
                : "Review the report, then open the issue link and paste the supplied text where the issue asks for it.",
        };
    }
}
