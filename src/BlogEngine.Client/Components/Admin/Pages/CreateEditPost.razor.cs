using System.Text.RegularExpressions;
using Blazored.FluentValidation;
using Blazored.Toast.Services;
using BlogEngine.Client.Services;
using BlogEngine.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NodaTime;
using NodaTime.Extensions;

namespace BlogEngine.Client.Components.Admin.Pages;

public partial class CreateEditPost : ComponentBase
{
    private FluentValidationValidator? _fluentValidationValidator;

    [SupplyParameterFromForm]
    private PostDto Dto { get; set; } = new();
    [Parameter] public int? PostId { get; set; }
    
    private string _messageResult = default!;
    private bool _eventSaved = false;
    private bool _isEditing = false;
    
    private PersistingComponentStateSubscription _persistingSubscription;

    [Inject] private ILogger<CreateEditPost> Logger { get; set; } = default!;
    [Inject] private HttpClient Client { get; set; }
    [Inject] private IToastService ToastService { get; set; }
    [Inject] private IJSRuntime JsRuntime { get; set; }
    [Inject] private NavigationManager NavigationManager { get; set; }
    [Inject] private PersistentComponentState PersistentComponentState { get; set; } = default!;
    [Inject] private IPostService PostService { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (PostId.HasValue)
        {
            _isEditing = true;
            if (Dto.PostId == default)
            {
                var foundInState =
                    PersistentComponentState.TryTakeFromJson<PostDto>(nameof(Dto), out var restoredEventData);
                Dto = foundInState ? restoredEventData! : await PostService.GetPostByIdAsync(PostId.Value);
            }
        }
        else
        {
            var clock = SystemClock.Instance;
            Dto.PublishDate =
                DateOnly.FromDateTime(clock.InTzdbSystemDefaultZone().GetCurrentLocalDateTime()
                    .ToDateTimeUnspecified());
        }
    }

    private async Task SavePost()
    {
        if (await _fluentValidationValidator.ValidateAsync())
            try
            {
                ToastService.ShowSuccess("Post was successfully saved.", settings => settings.DisableTimeout = true );
            }
            finally
            {
                
            }
        else
        {
            ToastService.ShowError("Unable to create the post, please correct the errors and try again.", settings => settings.DisableTimeout = true);
        }
    }
    
    private async Task UpdateSlug(string title)
    {
        Dto.Title = title;
        if (string.IsNullOrWhiteSpace(Dto.Slug))
        {
            title = title.ToLower();
            title = Regex.Replace(title, "[^a-zA-Z0-9 -]", "");
            title = title.Trim().Replace(' ', '-');
            Dto.Slug = title;
        }
    }

    private void ReplaceSlug()
    {
        var title = Dto.Title.ToLower();
        title = Regex.Replace(title, "[^a-zA-Z0-9 -]", "");
        title = title.Trim().Replace(' ', '-');
        Dto.Slug = title;
    }
}