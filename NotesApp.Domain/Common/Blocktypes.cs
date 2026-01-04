using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Type of parent entity for a Block.
    /// </summary>
    public enum BlockParentType
    {
        Note = 0,
        Task = 1
    }

    /// <summary>
    /// Type of content block.
    /// V1: Paragraph (0) and Image (1) only.
    /// Future types use reserved value ranges to allow insertion.
    /// </summary>
    public enum BlockType
    {
        // V1 - Core types
        Paragraph = 0,
        Image = 1,

        // V2+ - Future types (reserved values for ordering/grouping)
        Heading1 = 10,
        Heading2 = 11,
        Heading3 = 12,
        BulletList = 20,
        NumberedList = 21,
        Quote = 30,
        Code = 31,
        File = 40,
        Divider = 50
    }

    /// <summary>
    /// Status of asset upload for image/file blocks.
    /// </summary>
    public enum UploadStatus
    {
        /// <summary>
        /// Not applicable for text blocks.
        /// </summary>
        NotApplicable = 0,

        /// <summary>
        /// Asset metadata synced to server, binary upload not started.
        /// </summary>
        Pending = 1,

        /// <summary>
        /// Upload in progress (client-side state, not persisted on server).
        /// </summary>
        Uploading = 2,

        /// <summary>
        /// Asset successfully uploaded and linked.
        /// </summary>
        Synced = 3,

        /// <summary>
        /// Upload failed.
        /// </summary>
        Failed = 4
    }
}
