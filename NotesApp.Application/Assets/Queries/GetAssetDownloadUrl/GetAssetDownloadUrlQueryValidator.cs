using FluentValidation;

namespace NotesApp.Application.Assets.Queries.GetAssetDownloadUrl
{
    public sealed class GetAssetDownloadUrlQueryValidator : AbstractValidator<GetAssetDownloadUrlQuery>
    {
        public GetAssetDownloadUrlQueryValidator()
        {
            RuleFor(x => x.AssetId)
                .NotEmpty()
                .WithMessage("AssetId is required.");
        }
    }
}
