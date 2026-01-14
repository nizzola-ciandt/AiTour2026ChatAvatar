namespace AiTourBackend.Models;

public record SessionResponse(string SessionId);

public record AvatarOfferRequest(string Sdp);

public record AvatarAnswerResponse(string Sdp);

public record TextMessageRequest(string Text);

public record AudioCommitResponse(string Status);

public record HealthResponse(string Status, string Service);