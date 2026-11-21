using Microsoft.AspNetCore.Components;

namespace Blazix.Primitives;

/// <summary>How an autocomplete helps.</summary>
public enum AutocompleteMode
{
    /// <summary>The list shows the items that match what's typed.</summary>
    List,
    /// <summary>The list filters, and the highlighted item's text previews in the input.</summary>
    Both,
    /// <summary>The list shows every item, and the highlighted item's text previews in the input.</summary>
    Inline,
    /// <summary>The list shows every item; nothing previews.</summary>
    None,
}

/// <summary>
/// An input that suggests options as you type. Unlike a
/// combobox, its value is the input's text: choosing a suggestion just fills it in. Use the
/// Autocomplete parts (or the Combobox parts, which they are).
/// </summary>
public class AutocompleteRoot() : ComboboxRoot("autocomplete")
{
    /// <summary>Whether the list filters as you type, and whether the highlighted item previews in the input. Defaults to <see cref="AutocompleteMode.List"/>.</summary>
    [Parameter] public AutocompleteMode Mode { get; set; } = AutocompleteMode.List;

    internal override bool IsAutocomplete => true;

    private protected override bool Filters => Mode is AutocompleteMode.List or AutocompleteMode.Both;

    internal override bool InlinePreview => Mode is AutocompleteMode.Both or AutocompleteMode.Inline;

    protected override bool DefaultOpenOnInputClick => false;
}

#pragma warning disable S2094 // Aliases: the Autocomplete parts are Combobox's parts under the Autocomplete name.
/// <summary>The text input.</summary>
public class AutocompleteInput : ComboboxInput;

/// <summary>Wraps the input and its buttons.</summary>
public class AutocompleteInputGroup : ComboboxInputGroup;

/// <summary>A button that opens the suggestions.</summary>
public class AutocompleteTrigger : ComboboxTrigger;

/// <summary>An icon beside the input.</summary>
public class AutocompleteIcon : ComboboxIcon;

/// <summary>Clears the input.</summary>
public class AutocompleteClear : ComboboxClear;

/// <summary>The input's current text.</summary>
public class AutocompleteValue : ComboboxValue;

/// <summary>The suggestions popup.</summary>
public class AutocompletePopup : ComboboxPopup;

/// <summary>The list of suggestions.</summary>
public class AutocompleteList : ComboboxList;

/// <summary>A suggestion.</summary>
public class AutocompleteItem : ComboboxItem;

/// <summary>Rendered while nothing matches.</summary>
public class AutocompleteEmpty : ComboboxEmpty;

/// <summary>A live region for status messages.</summary>
public class AutocompleteStatus : ComboboxStatus;

/// <summary>Groups related suggestions.</summary>
public class AutocompleteGroup : ComboboxGroup;

/// <summary>A group's label.</summary>
public class AutocompleteGroupLabel : ComboboxGroupLabel;

/// <summary>A separator.</summary>
public class AutocompleteSeparator : ComboboxSeparator;

/// <summary>An arrow.</summary>
public class AutocompleteArrow : ComboboxArrow;

/// <summary>An overlay beneath the popup.</summary>
public class AutocompleteBackdrop : ComboboxBackdrop;
/// <summary>Positions the popup against its anchor.</summary>
public class AutocompletePositioner : ComboboxPositioner;

/// <summary>A portal for the popup.</summary>
public class AutocompletePortal : ComboboxPortal;

/// <summary>Renders the items that match what's typed.</summary>
public class AutocompleteCollection : ComboboxCollection;

/// <summary>A row of items in a grid list.</summary>
public class AutocompleteRow : ComboboxRow;
#pragma warning restore S2094
