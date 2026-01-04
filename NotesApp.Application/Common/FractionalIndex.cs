using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Common
{
    /// <summary>
    /// Generates lexicographically sortable position strings for ordering blocks.
    /// Based on Figma's fractional indexing algorithm.
    /// 
    /// Key properties:
    /// - Positions sort correctly with StringComparer.Ordinal
    /// - New positions can be inserted between any two existing positions
    /// - Appending/prepending generates short keys
    /// - Safe for billions of operations before key exhaustion
    /// 
    /// References:
    /// - https://www.figma.com/blog/realtime-editing-of-ordered-sequences/
    /// - https://observablehq.com/@dgreensp/implementing-fractional-indexing
    /// </summary>
    public static class FractionalIndex
    {
        // Base-62: 0-9, A-Z, a-z (lexicographically ordered in ASCII/ordinal)
        private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const char SmallestDigit = '0';
        private const char LargestDigit = 'z';
        private const int Base = 62;

        /// <summary>
        /// Generate the first position key.
        /// Use this when creating the first block in a parent.
        /// </summary>
        public static string First() => "a0";

        /// <summary>
        /// Generate a position after the given position.
        /// Use this when appending a block to the end.
        /// </summary>
        /// <param name="position">The current last position, or null/empty for first item.</param>
        /// <returns>A new position that sorts after the given position.</returns>
        public static string After(string? position)
        {
            if (string.IsNullOrEmpty(position))
                return First();

            // Try to increment the last character
            var chars = position.ToCharArray();
            for (int i = chars.Length - 1; i >= 0; i--)
            {
                var idx = Digits.IndexOf(chars[i]);
                if (idx < 0)
                    throw new ArgumentException($"Invalid character '{chars[i]}' in position string.", nameof(position));

                if (idx < Base - 1)
                {
                    chars[i] = Digits[idx + 1];
                    return new string(chars, 0, i + 1);
                }
            }

            // All characters at max - append smallest digit
            return position + SmallestDigit;
        }


        /// <summary>
        /// Generate a position before the given position.
        /// Use this when prepending a block to the beginning.
        /// </summary>
        /// <param name="position">The current first position, or null/empty for first item.</param>
        /// <returns>A new position that sorts before the given position.</returns>
        public static string Before(string? position)
        {
            if (string.IsNullOrEmpty(position))
                return First();

            // Try to decrement the last non-smallest character
            var chars = position.ToCharArray();
            for (int i = chars.Length - 1; i >= 0; i--)
            {
                var idx = Digits.IndexOf(chars[i]);
                if (idx < 0)
                    throw new ArgumentException($"Invalid character '{chars[i]}' in position string.", nameof(position));

                if (idx > 0)
                {
                    chars[i] = Digits[idx - 1];
                    var result = new string(chars, 0, i + 1);
                    // Trim trailing smallest digits
                    return result.TrimEnd(SmallestDigit) is { Length: > 0 } trimmed
                        ? trimmed
                        : result;
                }
            }

            // Can't go lower with current prefix - use smaller prefix
            return "Z" + LargestDigit;
        }


        /// <summary>
        /// Generate a position between two existing positions.
        /// Use this when inserting a block between two existing blocks.
        /// </summary>
        /// <param name="before">Position of the block before, or null if inserting at start.</param>
        /// <param name="after">Position of the block after, or null if inserting at end.</param>
        /// <returns>A new position that sorts between the two given positions.</returns>
        /// <exception cref="ArgumentException">Thrown if before >= after.</exception>
        public static string Between(string? before, string? after)
        {
            if (string.IsNullOrEmpty(before) && string.IsNullOrEmpty(after))
                return First();

            if (string.IsNullOrEmpty(before))
                return Before(after);

            if (string.IsNullOrEmpty(after))
                return After(before);

            if (string.Compare(before, after, StringComparison.Ordinal) >= 0)
                throw new ArgumentException($"'before' ({before}) must be less than 'after' ({after}).");

            return Midpoint(before, after);
        }


        /// <summary>
        /// Generate multiple evenly-spaced positions between two bounds.
        /// Use this when inserting multiple blocks at once (e.g., paste operation).
        /// </summary>
        /// <param name="before">Position before the first new block, or null.</param>
        /// <param name="after">Position after the last new block, or null.</param>
        /// <param name="count">Number of positions to generate.</param>
        /// <returns>Array of positions in order.</returns>
        public static string[] GenerateN(string? before, string? after, int count)
        {
            if (count <= 0)
                return Array.Empty<string>();

            if (count == 1)
                return new[] { Between(before, after) };

            var results = new string[count];
            var prev = before;

            for (int i = 0; i < count; i++)
            {
                var next = Between(prev, after);
                results[i] = next;
                prev = next;
            }

            return results;
        }


        /// <summary>
        /// Validates that a position string contains only valid characters.
        /// </summary>
        /// <param name="position">Position string to validate.</param>
        /// <returns>True if valid, false otherwise.</returns>
        public static bool IsValid(string? position)
        {
            if (string.IsNullOrEmpty(position))
                return false;

            foreach (var c in position)
            {
                if (Digits.IndexOf(c) < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two position strings.
        /// </summary>
        /// <param name="a">First position.</param>
        /// <param name="b">Second position.</param>
        /// <returns>Negative if a &lt; b, zero if equal, positive if a &gt; b.</returns>
        public static int Compare(string? a, string? b)
        {
            return string.Compare(a, b, StringComparison.Ordinal);
        }


        private static string Midpoint(string a, string b)
        {
            var maxLen = Math.Max(a.Length, b.Length);
            var aPadded = a.PadRight(maxLen, SmallestDigit);
            var bPadded = b.PadRight(maxLen, LargestDigit);

            var result = new char[maxLen + 1];
            var resultLen = 0;

            for (int i = 0; i < maxLen; i++)
            {
                var aIdx = Digits.IndexOf(aPadded[i]);
                var bIdx = Digits.IndexOf(bPadded[i]);

                if (aIdx == bIdx)
                {
                    result[resultLen++] = aPadded[i];
                    continue;
                }

                var midIdx = (aIdx + bIdx) / 2;

                if (midIdx > aIdx)
                {
                    result[resultLen++] = Digits[midIdx];
                    return new string(result, 0, resultLen);
                }

                // No room at this position - need to go deeper
                result[resultLen++] = aPadded[i];

                // Recursively find midpoint for remaining part
                var remaining = MidpointSuffix(
                    aPadded.Substring(i + 1),
                    new string(LargestDigit, maxLen - i - 1));

                foreach (var c in remaining)
                    result[resultLen++] = c;

                return new string(result, 0, resultLen);
            }

            // Equal up to maxLen - add middle digit
            result[resultLen++] = Digits[Base / 2];
            return new string(result, 0, resultLen);
        }

        private static string MidpointSuffix(string a, string b)
        {
            // Simplified midpoint for suffix calculation
            if (a.Length == 0 || b.Length == 0)
                return Digits[Base / 2].ToString();

            var aIdx = Digits.IndexOf(a[0]);
            var bIdx = Digits.IndexOf(b[0]);

            if (aIdx < 0) aIdx = 0;
            if (bIdx < 0) bIdx = Base - 1;

            var midIdx = (aIdx + bIdx) / 2;

            if (midIdx > aIdx)
                return Digits[midIdx].ToString();

            // Need to go deeper
            return a[0] + MidpointSuffix(
                a.Length > 1 ? a.Substring(1) : string.Empty,
                new string(LargestDigit, Math.Max(0, b.Length - 1)));
        }
    }
}
