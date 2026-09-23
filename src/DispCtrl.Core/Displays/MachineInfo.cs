using Microsoft.Win32;

namespace DispCtrl.Core.Displays;

/// <summary>
/// What machine a built-in panel is built into.
/// </summary>
/// <remarks>
/// An internal display has almost no identity of its own: no DDC/CI channel, and
/// usually no EDID monitor-name descriptor either, so it reports its model as
/// "not reported" and there the trail ends. The laptop knows, though — the panel
/// in this one is the screen of an ASUS M7400QC, and that is the fact someone
/// searching a device folder for their own laptop would search by.
/// <para>
/// Read from the registry rather than WMI, for the same reason
/// <see cref="Edid"/> is: <c>System.Management</c> is not Native-AOT friendly
/// and this has to be callable from the engine.
/// </para>
/// <para>
/// <b>Only model-level values are read.</b> The same key holds
/// <c>SystemSerialNumber</c> and a machine UUID; neither is touched, because
/// neither would be the same on another machine of the same model and both
/// would go straight into a published record.
/// </para>
/// </remarks>
public sealed record MachineInfo
{
    public bool Present { get; init; }

    /// <summary>e.g. <c>ASUSTeK COMPUTER INC.</c></summary>
    public string Manufacturer { get; init; } = "";

    /// <summary>The short model anyone would recognise, e.g. <c>M7400QC</c>.</summary>
    public string Model { get; init; } = "";

    /// <summary>The long form the firmware gives, e.g. <c>Vivobook_ASUSLaptop M7400QC_M7400QC</c>.</summary>
    public string ProductName { get; init; } = "";

    public string Family { get; init; } = "";

    public string Sku { get; init; } = "";

    public string BaseBoard { get; init; } = "";

    public string BiosVendor { get; init; } = "";

    public string BiosVersion { get; init; } = "";

    public string BiosDate { get; init; } = "";

    /// <summary>Maker, family and model on one line.</summary>
    public string Label
    {
        get
        {
            if (!Present) return "";

            var parts = new List<string>(3);
            if (Manufacturer.Length > 0) parts.Add(Tidy(Manufacturer));
            if (Family.Length > 0 && !Model.Contains(Family, StringComparison.OrdinalIgnoreCase))
                parts.Add(Family);
            if (Model.Length > 0) parts.Add(Model);

            return string.Join(" ", parts);
        }
    }

    public static MachineInfo None => new() { Present = false };

    private static MachineInfo? _cached;
    private static readonly Lock Gate = new();

    /// <summary>Reads the firmware's own description of this machine.</summary>
    /// <remarks>Cached: it cannot change while Windows is running.</remarks>
    public static MachineInfo Read()
    {
        lock (Gate)
        {
            if (_cached is not null) return _cached;
        }

        MachineInfo read = ReadUncached();

        lock (Gate)
        {
            _cached = read;
        }

        return read;
    }

    private static MachineInfo ReadUncached()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\BIOS");

            if (key is null) return None;

            string product = Value(key, "SystemProductName");
            string board = Value(key, "BaseBoardProduct");

            return new MachineInfo
            {
                Present = product.Length > 0 || board.Length > 0,
                Manufacturer = Value(key, "SystemManufacturer"),
                Model = ShortModel(product, board),
                ProductName = product,
                Family = Value(key, "SystemFamily"),
                Sku = Value(key, "SystemSKU"),
                BaseBoard = board,
                BiosVendor = Value(key, "BIOSVendor"),
                BiosVersion = Value(key, "BIOSVersion"),
                BiosDate = Value(key, "BIOSReleaseDate"),
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return None;
        }
    }

    /// <summary>
    /// The shortest name that is still this machine.
    /// </summary>
    /// <remarks>
    /// The board name is preferred, but only when the product name corroborates
    /// it: this laptop reports the board as <c>M7400QC</c> and the product as
    /// <c>Vivobook_ASUSLaptop M7400QC_M7400QC</c>, so the short one is plainly
    /// the same machine said briefly. On a desktop the two describe different
    /// things — a motherboard is not a PC model — and the corroboration fails,
    /// so the product name is used as given.
    /// </remarks>
    private static string ShortModel(string product, string board)
    {
        if (board.Length > 0 && product.Contains(board, StringComparison.OrdinalIgnoreCase))
            return board;

        return product.Length > 0 ? product : board;
    }

    /// <summary>Drops the shouty legal suffix firmware vendors write.</summary>
    private static string Tidy(string maker)
    {
        foreach (string noise in (string[])[" COMPUTER INC.", " COMPUTER INC", ", LLC.", ", LLC", " INC.", " INC"])
            if (maker.EndsWith(noise, StringComparison.OrdinalIgnoreCase))
                return maker[..^noise.Length].Trim();

        return maker.Trim();
    }

    private static string Value(RegistryKey key, string name) =>
        key.GetValue(name) as string is { } s ? s.Trim() : "";
}
