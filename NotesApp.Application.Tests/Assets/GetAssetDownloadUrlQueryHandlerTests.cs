using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Assets.Queries.GetAssetDownloadUrl;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Domain.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Tests.Assets
{
    public sealed class GetAssetDownloadUrlQueryHandlerTests
    {
        private readonly Mock<IAssetRepository> _assetRepositoryMock = new();
        private readonly Mock<IBlobStorageService> _blobStorageServiceMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ILogger<GetAssetDownloadUrlQueryHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly AssetStorageOptions _assetOptions = new()
        {
            ContainerName = "test-assets",
            DownloadUrlValidityMinutes = 60
        };

        private GetAssetDownloadUrlQueryHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            return new GetAssetDownloadUrlQueryHandler(
                _assetRepositoryMock.Object,
                _blobStorageServiceMock.Object,
                _currentUserServiceMock.Object,
                Options.Create(_assetOptions),
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_AssetFound_ReturnsDownloadUrl()
        {
            // Arrange
            var handler = CreateHandler();
            var assetId = Guid.NewGuid();
            var expectedUrl = "https://storage.example.com/test-assets/path?sas=token";

            var asset = CreateAsset(_userId, assetId, blobPath: "users/file.jpg");

            _assetRepositoryMock
                .Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(asset);

            _blobStorageServiceMock
                .Setup(s => s.GenerateDownloadUrlAsync(
                    _assetOptions.ContainerName,
                    asset.BlobPath,
                    _assetOptions.DownloadUrlValidity,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok(expectedUrl));

            var query = new GetAssetDownloadUrlQuery(assetId);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedUrl);
        }

        [Fact]
        public async Task Handle_AssetNotFound_ReturnsAssetNotFoundError()
        {
            // Arrange
            var handler = CreateHandler();
            var assetId = Guid.NewGuid();

            _assetRepositoryMock
                .Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Asset?)null);

            var query = new GetAssetDownloadUrlQuery(assetId);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Asset.NotFound");

            _blobStorageServiceMock.Verify(
                s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_AssetBelongsToDifferentUser_ReturnsAssetNotFoundError()
        {
            // Arrange
            var handler = CreateHandler();
            var assetId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var asset = CreateAsset(otherUserId, assetId, blobPath: "users/file.jpg");

            _assetRepositoryMock
                .Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(asset);

            var query = new GetAssetDownloadUrlQuery(assetId);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Asset.NotFound");

            _blobStorageServiceMock.Verify(
                s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_AssetIsDeleted_ReturnsAssetNotFoundError()
        {
            // Arrange
            var handler = CreateHandler();
            var assetId = Guid.NewGuid();
            var utcNow = DateTime.UtcNow;

            var asset = CreateAsset(_userId, assetId, blobPath: "users/file.jpg");
            asset.SoftDelete(utcNow);

            _assetRepositoryMock
                .Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(asset);

            var query = new GetAssetDownloadUrlQuery(assetId);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Asset.NotFound");

            _blobStorageServiceMock.Verify(
                s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_BlobStorageFailure_ReturnsGenerationFailedError()
        {
            // Arrange
            var handler = CreateHandler();
            var assetId = Guid.NewGuid();

            var asset = CreateAsset(_userId, assetId, blobPath: "users/file.jpg");

            _assetRepositoryMock
                .Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(asset);

            _blobStorageServiceMock
                .Setup(s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Fail("Storage unavailable"));

            var query = new GetAssetDownloadUrlQuery(assetId);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Asset.DownloadUrl.GenerationFailed");
        }

        [Fact]
        public async Task Handle_CallsBlobStorageWithCorrectContainerAndPath()
        {
            // Arrange
            var handler = CreateHandler();
            var assetId = Guid.NewGuid();
            const string blobPath = "users/abc/blocks/xyz/photo.jpg";

            var asset = CreateAsset(_userId, assetId, blobPath);

            _assetRepositoryMock
                .Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(asset);

            _blobStorageServiceMock
                .Setup(s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok("https://url"));

            var query = new GetAssetDownloadUrlQuery(assetId);

            // Act
            await handler.Handle(query, CancellationToken.None);

            // Assert
            _blobStorageServiceMock.Verify(
                s => s.GenerateDownloadUrlAsync(
                    _assetOptions.ContainerName,
                    blobPath,
                    _assetOptions.DownloadUrlValidity,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static Asset CreateAsset(Guid userId, Guid assetId, string blobPath)
        {
            var result = Asset.Create(
                userId,
                Guid.NewGuid(),
                "file.jpg",
                "image/jpeg",
                1024,
                blobPath,
                DateTime.UtcNow);

            result.IsSuccess.Should().BeTrue();
            var asset = result.Value!;

            typeof(Asset).GetProperty(nameof(Asset.Id))!
                .SetValue(asset, assetId);

            return asset;
        }
    }
}
