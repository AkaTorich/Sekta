using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class StickersViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    public StickersViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    private ObservableCollection<StickerPackDto> _packs = [];

    [ObservableProperty]
    private StickerPackDto? _selectedPack;

    [ObservableProperty]
    private ObservableCollection<StickerDto> _stickers = [];

    public async Task LoadPacksAsync()
    {
        try
        {
            var packs = await _apiService.GetAsync<List<StickerPackDto>>("/api/stickers/packs");
            if (packs != null)
            {
                Packs = new ObservableCollection<StickerPackDto>(packs);
                if (packs.Count > 0)
                    SelectPack(packs[0]);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load sticker packs: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectPack(StickerPackDto? pack)
    {
        if (pack == null) return;
        SelectedPack = pack;
        Stickers = new ObservableCollection<StickerDto>(pack.Stickers);
    }

    [RelayCommand]
    private async Task SelectStickerAsync(StickerDto? sticker)
    {
        if (sticker == null) return;

        // Send sticker URL back to ChatPage via MessagingCenter
        MessagingCenter.Send(this, "StickerSelected", sticker);
        await Shell.Current.GoToAsync("..");
    }
}
