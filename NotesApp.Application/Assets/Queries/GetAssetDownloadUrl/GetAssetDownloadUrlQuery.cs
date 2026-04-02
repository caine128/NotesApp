using FluentResults;
using MediatR;

namespace NotesApp.Application.Assets.Queries.GetAssetDownloadUrl
{
    public sealed record GetAssetDownloadUrlQuery(Guid AssetId) : IRequest<Result<string>>;
}
