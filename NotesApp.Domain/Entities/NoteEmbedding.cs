using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Embedding for a note, used for semantic search and AI features.
    /// Invariants:
    /// - NoteId and UserId must be non-empty.
    /// - Model must be non-empty.
    /// - Vector must be non-null and have at least one element.
    /// - Dimension equals Vector.Length.
    /// </summary>
    public sealed class NoteEmbedding : Entity<Guid>
    {
        public Guid NoteId { get; private set; }
        public Guid UserId { get; private set; }

        /// <summary>
        /// Name or identifier of the embedding model (e.g. "text-embedding-3-large").
        /// </summary>
        // TODO: Should come from options/config?
        public string Model { get; private set; } = string.Empty;

        /// <summary>
        /// Dimension of the embedding vector. Must equal Vector.Length.
        /// </summary>
        public int Dimension { get; private set; }

        /// <summary>
        /// Embedding vector. Persisted via a value converter or an external store.
        /// </summary>
        public float[] Vector { get; private set; } = Array.Empty<float>();

        // EF Core constructor
        private NoteEmbedding()
        {
        }

        private NoteEmbedding(Guid id,
                              Guid noteId,
                              Guid userId,
                              string model,
                              float[] vector,
                              DateTime utcNow)
            : base(id, utcNow)
        {
            NoteId = noteId;
            UserId = userId;
            Model = model;
            Vector = vector;
            Dimension = vector.Length;
        }

        /// <summary>
        /// Create a new embedding for a note.
        /// </summary>
        public static DomainResult<NoteEmbedding> Create(Guid noteId,
                                                         Guid userId,
                                                         string model,
                                                         float[] vector,
                                                         DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (noteId == Guid.Empty)
            {
                errors.Add(new DomainError(
                    "NoteEmbedding.NoteId.Empty",
                    "NoteId must be a non-empty GUID."));
            }

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError(
                    "NoteEmbedding.UserId.Empty",
                    "UserId must be a non-empty GUID."));
            }

            var normalizedModel = model?.Trim() ?? string.Empty;
            if (normalizedModel.Length == 0)
            {
                errors.Add(new DomainError(
                    "NoteEmbedding.Model.Empty",
                    "Model must be a non-empty string."));
            }

            if (vector is null || vector.Length == 0)
            {
                errors.Add(new DomainError(
                    "NoteEmbedding.Vector.Empty",
                    "Vector must be a non-null array with at least one element."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<NoteEmbedding>.Failure(errors);
            }

            // Defensive copy so callers can't mutate internal state.
            var vectorCopy = vector.ToArray();

            var id = Guid.NewGuid();
            var embedding = new NoteEmbedding(id,
                                              noteId,
                                              userId,
                                              normalizedModel,
                                              vectorCopy,
                                              utcNow);

            return DomainResult<NoteEmbedding>.Success(embedding);
        }

        /// <summary>
        /// Replace the embedding vector (for example, when re-embedding with a newer model).
        /// </summary>
        public DomainResult UpdateVector(string model,
                                         float[] vector,
                                         DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedModel = model?.Trim() ?? string.Empty;
            if (normalizedModel.Length == 0)
            {
                errors.Add(new DomainError(
                    "NoteEmbedding.Model.Empty",
                    "Model must be a non-empty string."));
            }

            if (vector is null || vector.Length == 0)
            {
                errors.Add(new DomainError(
                    "NoteEmbedding.Vector.Empty",
                    "Vector must be a non-null array with at least one element."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            var vectorCopy = vector.ToArray();

            Model = normalizedModel;
            Vector = vectorCopy;
            Dimension = vectorCopy.Length;
            Touch(utcNow);

            return DomainResult.Success();
        }
    }
}
