namespace MiniBank.Web.Models;

public record PreviewRequest(
    long TypeId,
    decimal AmountTotal,
    decimal? Principal,
    decimal? Interest,
    decimal? Fee,
    decimal? Tax,
    long? MemberId);
