using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlogEngine.Client.Components;

public partial class MuiInputText : InputBase<string>
{
    [Parameter] [EditorRequired] public string Label { get; set; }

    [Parameter] public string Type { get; set; } = "text";
    
    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }
}