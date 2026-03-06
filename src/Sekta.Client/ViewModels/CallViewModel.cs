using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class CallViewModel : ObservableObject, IQueryAttributable
{
    private readonly ISignalRService _signalRService;
    private System.Timers.Timer? _durationTimer;
    private DateTime _callStartTime;

    public CallViewModel(ISignalRService signalRService)
    {
        _signalRService = signalRService;

        _signalRService.CallEnded += OnCallEnded;
        _signalRService.CallRejected += OnCallRejected;
    }

    [ObservableProperty]
    private Guid _callId;

    [ObservableProperty]
    private string _callerName = string.Empty;

    [ObservableProperty]
    private string? _callerAvatarUrl;

    [ObservableProperty]
    private string _callStatus = "Calling...";

    [ObservableProperty]
    private string _callDuration = "00:00";

    [ObservableProperty]
    private bool _isCallActive;

    [ObservableProperty]
    private bool _isIncoming;

    [ObservableProperty]
    private bool _isVideoCall;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isSpeakerOn;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("callId", out var idObj) && idObj is string idStr && Guid.TryParse(idStr, out var id))
            CallId = id;

        if (query.TryGetValue("callerName", out var nameObj) && nameObj is string name)
            CallerName = name;

        if (query.TryGetValue("isIncoming", out var incObj) && incObj is string inc)
            IsIncoming = inc == "true";

        if (query.TryGetValue("isVideo", out var vidObj) && vidObj is string vid)
            IsVideoCall = vid == "true";

        if (query.TryGetValue("targetUserId", out var targetObj) && targetObj is string targetStr)
        {
            if (Guid.TryParse(targetStr, out var targetId))
                _ = StartOutgoingCallAsync(targetId);
        }
    }

    private async Task StartOutgoingCallAsync(Guid targetUserId)
    {
        CallStatus = "Calling...";
        // WebRTC signaling would happen here
        // For now, the SignalR CallHub handles the signaling
    }

    [RelayCommand]
    private async Task AnswerCallAsync()
    {
        try
        {
            IsIncoming = false;
            IsCallActive = true;
            CallStatus = "Connected";
            StartDurationTimer();

            // In a real app, send SDP answer via CallHub
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to answer call: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RejectCallAsync()
    {
        try
        {
            await Task.CompletedTask; // CallHub.RejectCall would be called
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to reject call: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task EndCallAsync()
    {
        try
        {
            StopDurationTimer();
            await Task.CompletedTask; // CallHub.EndCall would be called
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to end call: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void ToggleSpeaker()
    {
        IsSpeakerOn = !IsSpeakerOn;
    }

    [RelayCommand]
    private void ToggleVideo()
    {
        IsVideoCall = !IsVideoCall;
    }

    private void StartDurationTimer()
    {
        _callStartTime = DateTime.Now;
        _durationTimer = new System.Timers.Timer(1000);
        _durationTimer.Elapsed += (s, e) =>
        {
            var elapsed = DateTime.Now - _callStartTime;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CallDuration = elapsed.ToString(@"mm\:ss");
            });
        };
        _durationTimer.Start();
    }

    private void StopDurationTimer()
    {
        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _durationTimer = null;
    }

    private void OnCallEnded(Guid callId)
    {
        if (callId != CallId) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            StopDurationTimer();
            CallStatus = "Call ended";
            await Task.Delay(1500);
            await Shell.Current.GoToAsync("..");
        });
    }

    private void OnCallRejected(Guid callId)
    {
        if (callId != CallId) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            CallStatus = "Call rejected";
            await Task.Delay(1500);
            await Shell.Current.GoToAsync("..");
        });
    }
}
