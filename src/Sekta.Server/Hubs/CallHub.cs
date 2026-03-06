using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Hubs;

[Authorize]
public class CallHub : Hub
{
    private readonly IUserService _userService;

    private static readonly Dictionary<Guid, CallSession> _activeCalls = new();
    private static readonly object _lock = new();

    public CallHub(IUserService userService)
    {
        _userService = userService;
    }

    private Guid GetUserId() =>
        Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task StartCall(StartCallDto dto)
    {
        var callerId = GetUserId();
        var caller = await _userService.GetUser(callerId);
        var callId = Guid.NewGuid();

        lock (_lock)
        {
            _activeCalls[callId] = new CallSession
            {
                CallId = callId,
                CallerId = callerId,
                TargetId = dto.TargetUserId,
                IsVideo = dto.IsVideo
            };
        }

        var targetConnections = ChatHub.GetUserConnections(dto.TargetUserId.ToString());
        foreach (var connId in targetConnections)
        {
            await Clients.Client(connId).SendAsync("IncomingCall", new CallOfferDto(
                callId, callerId, caller.DisplayName ?? caller.Username, dto.IsVideo, ""
            ));
        }
    }

    public async Task SendOffer(Guid callId, string sdp)
    {
        CallSession? session;
        lock (_lock)
        {
            _activeCalls.TryGetValue(callId, out session);
        }
        if (session == null) return;

        var targetConnections = ChatHub.GetUserConnections(session.TargetId.ToString());
        foreach (var connId in targetConnections)
        {
            await Clients.Client(connId).SendAsync("ReceiveOffer", callId, sdp);
        }
    }

    public async Task SendAnswer(CallAnswerDto dto)
    {
        CallSession? session;
        lock (_lock)
        {
            _activeCalls.TryGetValue(dto.CallId, out session);
        }
        if (session == null) return;

        var callerConnections = ChatHub.GetUserConnections(session.CallerId.ToString());
        foreach (var connId in callerConnections)
        {
            await Clients.Client(connId).SendAsync("ReceiveAnswer", dto.CallId, dto.Sdp);
        }
    }

    public async Task SendIceCandidate(IceCandidateDto dto)
    {
        CallSession? session;
        lock (_lock)
        {
            _activeCalls.TryGetValue(dto.CallId, out session);
        }
        if (session == null) return;

        var userId = GetUserId();
        var targetId = session.CallerId == userId ? session.TargetId : session.CallerId;
        var targetConnections = ChatHub.GetUserConnections(targetId.ToString());

        foreach (var connId in targetConnections)
        {
            await Clients.Client(connId).SendAsync("ReceiveIceCandidate", dto);
        }
    }

    public async Task EndCall(Guid callId)
    {
        CallSession? session;
        lock (_lock)
        {
            _activeCalls.Remove(callId, out session);
        }
        if (session == null) return;

        var userId = GetUserId();
        var targetId = session.CallerId == userId ? session.TargetId : session.CallerId;
        var targetConnections = ChatHub.GetUserConnections(targetId.ToString());

        foreach (var connId in targetConnections)
        {
            await Clients.Client(connId).SendAsync("CallEnded", callId);
        }
    }

    public async Task RejectCall(Guid callId)
    {
        CallSession? session;
        lock (_lock)
        {
            _activeCalls.Remove(callId, out session);
        }
        if (session == null) return;

        var callerConnections = ChatHub.GetUserConnections(session.CallerId.ToString());
        foreach (var connId in callerConnections)
        {
            await Clients.Client(connId).SendAsync("CallRejected", callId);
        }
    }

    private class CallSession
    {
        public Guid CallId { get; set; }
        public Guid CallerId { get; set; }
        public Guid TargetId { get; set; }
        public bool IsVideo { get; set; }
    }
}
