using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class SelectableContact : ObservableObject
{
    public UserDto User { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SelectableContact(UserDto user)
    {
        User = user;
    }
}

public partial class CreateGroupViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    public CreateGroupViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    private string _groupName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SelectableContact> _contacts = [];

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _isCreating;

    public async Task LoadContactsAsync()
    {
        try
        {
            var contacts = await _apiService.GetAsync<List<UserDto>>($"{ApiRoutes.Users}/contacts");
            if (contacts is not null)
            {
                Contacts = new ObservableCollection<SelectableContact>(
                    contacts.Select(c => new SelectableContact(c)));
            }
            SelectedCount = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load contacts: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleMember(SelectableContact contact)
    {
        if (contact is null) return;
        contact.IsSelected = !contact.IsSelected;
        SelectedCount = Contacts.Count(c => c.IsSelected);
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            await Shell.Current.DisplayAlert("Error", "Please enter a group name.", "OK");
            return;
        }

        var selected = Contacts.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await Shell.Current.DisplayAlert("Error", "Please select at least one member.", "OK");
            return;
        }

        try
        {
            IsCreating = true;

            var dto = new CreateGroupChatDto(
                GroupName.Trim(),
                selected.Select(c => c.User.Id).ToList());

            var chat = await _apiService.PostAsync<ChatDto>($"{ApiRoutes.Chats}/group", dto);

            if (chat is not null)
            {
                // Navigate back to chats list — it will refresh and show the new group
                await Shell.Current.GoToAsync("//main/chats");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create group: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", "Failed to create group chat.", "OK");
        }
        finally
        {
            IsCreating = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
