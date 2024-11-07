using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Blazored.FluentValidation;
using Blazored.Toast.Services;
using BlogEngine.Client.Services;
using BlogEngine.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NodaTime;
using NodaTime.Extensions;
using SharedConstants = BlogEngine.Shared.Models.Constants;

namespace BlogEngine.Client.Components.Admin.Pages;

public partial class CreateEditPost : ComponentBase, IDisposable
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
        HttpResponseMessage? result = null;

        if (await _fluentValidationValidator.ValidateAsync())
        {
            if (Dto.PostId > 0)
            {
                result = await Client.PutAsJsonAsync($"{SharedConstants.PostApiUrl}/update/{PostId}", Dto);
            }
            else
            {
                result = await Client.PostAsJsonAsync($"{SharedConstants.PostApiUrl}/create", Dto);
            }

            if (result.IsSuccessStatusCode)
            {
                // ToastService.ShowSuccess("The Event has been saved off correctly.");
                NavigationManager.NavigateTo("/admin/posts");
            }
            else
            {
                ToastService.ShowError("Unable to save the post, please correct the errors and try again.");
            }
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
    
    void IDisposable.Dispose() => _persistingSubscription.Dispose();
}