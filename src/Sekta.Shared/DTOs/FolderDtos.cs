namespace Sekta.Shared.DTOs;

public record ChatFolderDto(Guid Id, string Name, string Icon, int SortOrder, List<Guid> ChatIds);

public record CreateFolderDto(string Name, string? Icon);

public record UpdateFolderDto(string? Name, string? Icon, int? SortOrder);
