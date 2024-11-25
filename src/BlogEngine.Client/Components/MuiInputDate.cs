using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlogEngine.Client.Components;

public class MuiInputDate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TValue> : InputBase<TValue>
{
    private const string DateFormat = "yyyy-MM-dd";                     // Compatible with HTML 'date' inputs
    private const string DateTimeLocalFormat = "yyyy-MM-ddTHH:mm:ss";   // Compatible with HTML 'datetime-local' inputs
    private const string MonthFormat = "yyyy-MM";                       // Compatible with HTML 'month' inputs
    private const string TimeFormat = "HH:mm:ss";                       // Compatible with HTML 'time' inputs

    private string _typeAttributeValue = default!;
    private string _format = default!;
    private string _parsingErrorMessage = default!;

    /// <summary>
    /// Gets or sets the type of HTML input to be rendered.
    /// </summary>
    [Parameter] public InputDateType Type { get; set; } = InputDateType.Date;

    [Parameter, EditorRequired] public string Label { get; set; } = default!;
    /// <summary>
    /// Gets or sets the error message used when displaying an a parsing error.
    /// </summary>
    [Parameter] public string ParsingErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the associated <see cref="ElementReference"/>.
    /// <para>
    /// May be <see langword="null"/> if accessed before the component is rendered.
    /// </para>
    /// </summary>
    [DisallowNull] public ElementReference? Element { get; protected set; }

    /// <summary>
    /// Constructs an instance of <see cref="InputDate{TValue}"/>
    /// </summary>
    public MuiInputDate()
    {
        var type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        if (type != typeof(DateTime) &&
            type != typeof(DateTimeOffset) &&
            type != typeof(DateOnly) &&
            type != typeof(TimeOnly))
        {
            throw new InvalidOperationException($"Unsupported {GetType()} type param '{type}'.");
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        (_typeAttributeValue, _format, var formatDescription) = Type switch
        {
            InputDateType.Date => ("date", DateFormat, "date"),
            InputDateType.DateTimeLocal => ("datetime-local", DateTimeLocalFormat, "date and time"),
            InputDateType.Month => ("month", MonthFormat, "year and month"),
            InputDateType.Time => ("time", TimeFormat, "time"),
            _ => throw new InvalidOperationException($"Unsupported {nameof(InputDateType)} '{Type}'.")
        };

        _parsingErrorMessage = string.IsNullOrEmpty(ParsingErrorMessage)
            ? $"The {{0}} field must be a {formatDescription}."
            : ParsingErrorMessage;
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "relative w-full min-w-[200px] h-10 mt-4");
        builder.OpenElement(2, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", _typeAttributeValue);
        builder.AddAttribute(3, "name", NameAttributeValue);
        builder.AddAttribute(4, "id", NameAttributeValue);
        builder.AddAttribute(5, "class", $"peer w-full h-full bg-transparent text-white font-normal outline outline-0 focus:outline-0 disabled:bg-slate-50 disabled:border-0 transition-all placeholder-shown:border placeholder-shown:border-slate-200 placeholder-shown:border-t-slate-200 border focus:border-2 border-t-transparent focus:border-t-transparent text-sm px-3 py-2.5 rounded-[7px] border-slate-200 focus:border-slate-300 {CssClass}");
        builder.AddAttribute(6, "value", CurrentValueAsString);
        builder.AddAttribute(7, "onchange", EventCallback.Factory.CreateBinder<string?>(this, __value => CurrentValueAsString = __value, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.AddElementReferenceCapture(7, __inputReference => Element = __inputReference);
        builder.CloseElement();
        builder.OpenElement(3, "label");
        builder.AddAttribute(1, "for", NameAttributeValue);
        builder.AddAttribute(2, "class", $"flex w-full h-full select-none pointer-events-none absolute left-0 font-normal !overflow-visible truncate peer-placeholder-shown:text-orange-300 leading-tight peer-focus:leading-tight peer-disabled:text-transparent peer-disabled:peer-placeholder-shown:text-orange-300 transition-all -top-1.5 peer-placeholder-shown:text-sm text-[11px] peer-focus:text-[11px] before:block before:box-border before:w-2.5 before:h-1.5 before:mt-[6.5px] before:mr-1 peer-placeholder-shown:before:border-transparent before:rounded-tl-md before:border-t peer-focus:before:border-t-2 before:border-l peer-focus:before:border-l-2 before:pointer-events-none before:transition-all peer-disabled:before:border-transparent after:block after:flex-grow after:box-border after:w-2.5 after:h-1.5 after:mt-[6.5px] after:ml-1 peer-placeholder-shown:after:border-transparent after:rounded-tr-md after:border-t peer-focus:after:border-t-2 after:border-r peer-focus:after:border-r-2 after:pointer-events-none after:transition-all peer-disabled:after:border-transparent peer-placeholder-shown:leading-[3.75] text-orange-300 peer-focus:text-slate-300 before:border-slate-200 peer-focus:before:!border-slate-300 after:border-slate-200 peer-focus:after:!border-slate-300 {CssClass}");
        builder.AddContent(3, Label);
        builder.CloseElement();
        builder.CloseElement();
    }

    /// <inheritdoc />
    protected override string FormatValueAsString(TValue? value)
        => value switch
        {
            DateTime dateTimeValue => BindConverter.FormatValue(dateTimeValue, _format, CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffsetValue => BindConverter.FormatValue(dateTimeOffsetValue, _format, CultureInfo.InvariantCulture),
            DateOnly dateOnlyValue => BindConverter.FormatValue(dateOnlyValue, _format, CultureInfo.InvariantCulture),
            TimeOnly timeOnlyValue => BindConverter.FormatValue(timeOnlyValue, _format, CultureInfo.InvariantCulture),
            _ => string.Empty, // Handles null for Nullable<DateTime>, etc.
        };

    /// <inheritdoc />
    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (BindConverter.TryConvertTo(value, CultureInfo.InvariantCulture, out result))
        {
            Debug.Assert(result != null);
            validationErrorMessage = null;
            return true;
        }
        else
        {
            validationErrorMessage = string.Format(CultureInfo.InvariantCulture, _parsingErrorMessage, DisplayName ?? FieldIdentifier.FieldName);
            return false;
        }
    }
}