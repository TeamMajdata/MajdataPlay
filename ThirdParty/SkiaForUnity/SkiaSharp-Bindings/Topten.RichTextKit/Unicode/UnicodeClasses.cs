// RichTextKit
// Copyright © 2019-2020 Topten Software. All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may 
// not use this product except in compliance with the License. You may obtain 
// a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
// License for the specific language governing permissions and limitations 
// under the License.
using UnityEngine;

namespace Topten.RichTextKit
{
    /// <summary>
    /// Helper for looking up unicode character class information
    /// </summary>
    internal static class UnicodeClasses
    {
        static UnicodeClasses() {
            TextAsset bidiClassesAsset = Resources.Load<TextAsset>("BidiClasses");
            if (bidiClassesAsset != null) {
                byte[] data = bidiClassesAsset.bytes;
                _bidiTrie = new UnicodeTrie(data);
            }

            TextAsset LineBreakClasses = Resources.Load<TextAsset>("LineBreakClasses");
            if (LineBreakClasses != null) {
                byte[] data = LineBreakClasses.bytes;
                // Load trie resources
                _classesTrie = new UnicodeTrie(data);
            }

            TextAsset WordBoundaryClasses = Resources.Load<TextAsset>("WordBoundaryClasses");
            if (WordBoundaryClasses != null) {
                byte[] data = WordBoundaryClasses.bytes;
                _boundaryTrie = new UnicodeTrie(data);
            }


            TextAsset GraphemeClusterClasses = Resources.Load<TextAsset>("GraphemeClusterClasses");
            if (GraphemeClusterClasses != null) {
                byte[] data = GraphemeClusterClasses.bytes;
                _graphemeTrie = new UnicodeTrie(data);

            }

        }

        static UnicodeTrie _bidiTrie;
        static UnicodeTrie _classesTrie;
        static UnicodeTrie _boundaryTrie;
        static UnicodeTrie _graphemeTrie;

        /// <summary>
        /// Get the directionality of a Unicode Code Point
        /// </summary>
        /// <param name="codePoint">The code point in question</param>
        /// <returns>The code point's directionality</returns>
        public static Directionality Directionality(int codePoint)
        {
            return (Directionality)(_bidiTrie.Get(codePoint) >> 24);
        }

        /// <summary>
        /// Get the directionality of a Unicode Code Point
        /// </summary>
        /// <param name="codePoint">The code point in question</param>
        /// <returns>The code point's directionality</returns>
        public static uint BidiData(int codePoint)
        {
            return _bidiTrie.Get(codePoint);
        }

        /// <summary>
        /// Get the bracket type for a Unicode Code Point
        /// </summary>
        /// <param name="codePoint">The code point in question</param>
        /// <returns>The code point's paired bracked type</returns>
        public static PairedBracketType PairedBracketType(int codePoint)
        {
            return (PairedBracketType)((_bidiTrie.Get(codePoint) >> 16) & 0xFF);
        }

        /// <summary>
        /// Get the associated bracket type for a Unicode Code Point
        /// </summary>
        /// <param name="codePoint">The code point in question</param>
        /// <returns>The code point's opposite bracket, or 0 if not a bracket</returns>
        public static int AssociatedBracket(int codePoint)
        {
            return (int)(_bidiTrie.Get(codePoint) & 0xFFFF);
        }

        /// <summary>
        /// Get the line break class for a Unicode Code Point
        /// </summary>
        /// <param name="codePoint">The code point in question</param>
        /// <returns>The code point's line break class</returns>
        public static LineBreakClass LineBreakClass(int codePoint)
        {
            return (LineBreakClass)_classesTrie.Get(codePoint);
        }

        /// <summary>
        /// Get the line break class for a Unicode Code Point
        /// </summary>
        /// <param name="codePoint">The code point in question</param>
        /// <returns>The code point's line break class</returns>
        public static WordBoundaryClass BoundaryGroup(int codePoint)
        {
            return (WordBoundaryClass)_boundaryTrie.Get(codePoint);
        }

        /// <summary>
        /// Get the grapheme cluster class for a Unicode Code Point
        /// </summary>
        /// <param name="codePoint">The code point in question</param>
        /// <returns>The code point's grapheme cluster class</returns>
        public static GraphemeClusterClass GraphemeClusterClass(int codePoint)
        {
            return (GraphemeClusterClass)_graphemeTrie.Get(codePoint);
        }
    }
}
