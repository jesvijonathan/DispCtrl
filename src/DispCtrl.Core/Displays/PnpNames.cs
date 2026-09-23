namespace DispCtrl.Core.Displays;

/// <summary>
/// Expands the three-letter EDID manufacturer code into a company name.
/// </summary>
/// <remarks>
/// The code is an allocation from the PNP ID registry and nothing in the EDID
/// expands it, so this has to be a table. It is deliberately a short one:
/// display and panel makers, not the several thousand codes ever issued. A code
/// that is not here is reported as not known rather than guessed at, because a
/// wrong manufacturer in a device record is worse than none — it is the field
/// the record is filed under.
/// <para>
/// <b>The registry also records a country per code, and this does not.</b> That
/// is not an omission that can be fixed by trying harder: the country is part of
/// the registration, not of the EDID, and a panel assembled in one country by a
/// company registered in another would be described wrongly either way.
/// </para>
/// </remarks>
public static class PnpNames
{
    /// <summary>The company behind a code, or an empty string when unknown.</summary>
    public static string For(string code) =>
        code.Length == 3 && Known.TryGetValue(code, out string? name) ? name : "";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AAC"] = "AcerView",
        ["ACI"] = "Asus",
        ["ACR"] = "Acer",
        ["ADI"] = "ADI",
        ["AGO"] = "AGON by AOC",
        ["AMZ"] = "Amazon",
        ["ANX"] = "Analogix",
        ["AOC"] = "AOC",
        ["APP"] = "Apple",
        ["AUO"] = "AU Optronics",
        ["AUS"] = "Asus",
        ["BBY"] = "Insignia",
        ["BNQ"] = "BenQ",
        ["BOE"] = "BOE Technology",
        ["CMN"] = "Chi Mei Innolux",
        ["CMO"] = "Chi Mei Optoelectronics",
        ["CPQ"] = "Compaq",
        ["CPT"] = "Chunghwa Picture Tubes",
        ["CRO"] = "Corsair",
        ["CSO"] = "Samsung Display (CSOT)",
        ["DEL"] = "Dell",
        ["DON"] = "Denon",
        ["EIZ"] = "EIZO",
        ["ELE"] = "Element",
        ["ENC"] = "Eizo Nanao",
        ["EPI"] = "Envision",
        ["FUS"] = "Fujitsu Siemens",
        ["GBT"] = "Gigabyte",
        ["GSM"] = "LG Electronics",
        ["GWD"] = "Greenwood",
        ["GWY"] = "Gateway",
        ["HAT"] = "Hannspree",
        ["HEC"] = "Hisense",
        ["HPN"] = "HP",
        ["HSD"] = "Hannspree",
        ["HSP"] = "HannStar",
        ["HTC"] = "Hitachi",
        ["HWP"] = "HP",
        ["IVM"] = "Iiyama",
        ["KOA"] = "Konka",
        ["LEN"] = "Lenovo",
        ["LGD"] = "LG Display",
        ["LNX"] = "Lenovo",
        ["LPL"] = "LG Philips",
        ["MED"] = "Medion",
        ["MEI"] = "Panasonic",
        ["MEL"] = "Mitsubishi",
        ["MSI"] = "MSI",
        ["MST"] = "MStar",
        ["NCP"] = "Nexcom",
        ["NEC"] = "NEC",
        ["NVD"] = "Nvidia",
        ["ONN"] = "onn.",
        ["PHL"] = "Philips",
        ["PIO"] = "Pioneer",
        ["PLN"] = "Planar",
        ["PRI"] = "Primeview",
        ["QDS"] = "Quanta Display",
        ["RTK"] = "Realtek",
        ["SAM"] = "Samsung",
        ["SDC"] = "Samsung Display",
        ["SEC"] = "Seiko Epson",
        ["SHP"] = "Sharp",
        ["SNY"] = "Sony",
        ["SPT"] = "Sceptre",
        ["STN"] = "Samtron",
        ["SYN"] = "Synaptics",
        ["TCL"] = "TCL",
        ["TOP"] = "Top Victory",
        ["TSB"] = "Toshiba",
        ["UPD"] = "Uniprint",
        ["VES"] = "Vestel",
        ["VIZ"] = "Vizio",
        ["VSC"] = "ViewSonic",
        ["XMD"] = "Xiaomi",
        ["YMH"] = "Yamaha",
    };
}
