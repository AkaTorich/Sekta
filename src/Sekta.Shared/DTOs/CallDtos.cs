namespace Sekta.Shared.DTOs;

public record StartCallDto(Guid TargetUserId, bool IsVideo);

public record CallOfferDto(Guid CallId, Guid CallerId, string CallerName, bool IsVideo, string Sdp);

public record CallAnswerDto(Guid CallId, string Sdp);

public record IceCandidateDto(Guid CallId, string Candidate, string SdpMid, int SdpMLineIndex);
