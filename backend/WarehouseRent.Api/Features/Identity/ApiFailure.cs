namespace WarehouseRent.Api.Features.Identity;

public static class ApiFailure
{
    public static IResult Result(int status, string code, string detail) => Results.Problem(statusCode: status,
        title: "Request could not be completed", detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
