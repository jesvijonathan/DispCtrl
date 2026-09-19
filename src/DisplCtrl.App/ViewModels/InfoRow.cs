using Microsoft.UI.Xaml;

namespace DisplCtrl.App.ViewModels;

/// <summary>One line of a display's information table.</summary>
/// <remarks>
/// The table is built from these rather than written out as markup. Thirty-odd
/// hand-written label/value pairs is what the block used to be, and it had two
/// problems the markup could not fix: the copy button would have had to repeat
/// every label in C# to produce the same text, and each pair sized its own
/// column, so nothing lined up with anything below it. Rows give one fixed
/// label column and one source of truth for both the screen and the clipboard.
/// </remarks>
public sealed class InfoRow
{
    private InfoRow(string label, string value, bool section, bool identifier)
    {
        Label = label;
        Value = value;
        IsSection = section;
        IsIdentifier = identifier;
    }

    /// <summary>A heading spanning both columns, e.g. "The panel".</summary>
    public static InfoRow Heading(string title) => new(title, string.Empty, true, false);

    /// <summary>A label and what the display said.</summary>
    public static InfoRow Of(string label, string value) => new(label, value, false, false);

    /// <summary>
    /// A label and a machine identifier, set in a monospaced face.
    /// </summary>
    /// <remarks>
    /// Serials, device names and tokens are read character by character when
    /// they are read at all — a proportional face makes <c>l</c> and <c>1</c>
    /// the same shape, which is exactly the wrong thing for a string someone is
    /// about to type into a warranty form.
    /// </remarks>
    public static InfoRow Identifier(string label, string value) => new(label, value, false, true);

    public string Label { get; }

    public string Value { get; }

    public bool IsSection { get; }

    public bool IsIdentifier { get; }

    public Visibility SectionVisibility => IsSection ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FieldVisibility => IsSection ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ProseVisibility =>
        !IsSection && !IsIdentifier ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IdentifierVisibility =>
        !IsSection && IsIdentifier ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The row as one line of text, for the clipboard.</summary>
    public string AsText => IsSection ? $"[{Label}]" : $"{Label,-26}{Value}";
}
