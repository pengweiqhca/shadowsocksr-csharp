/*
* Copyright 2007 ZXing authors
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*      http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

namespace ZXing.QrCode.Internal
{
    /// <summary>
    /// <p>See ISO 18004:2006, 6.4.1, Tables 2 and 3. This enum encapsulates the various modes in which
    /// data can be encoded to bits in the QR code standard.</p>
    /// </summary>
    /// <author>Sean Owen</author>
    public sealed class Mode
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        public string Name
        {
            get => name;
        }

        // No, we can't use an enum here. J2ME doesn't support it.

        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode TERMINATOR = new(new[] { 0, 0, 0 }, 0x00, "TERMINATOR"); // Not really a mode...
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode NUMERIC = new(new[] { 10, 12, 14 }, 0x01, "NUMERIC");
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode ALPHANUMERIC = new(new[] { 9, 11, 13 }, 0x02, "ALPHANUMERIC");
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode STRUCTURED_APPEND = new(new[] { 0, 0, 0 }, 0x03, "STRUCTURED_APPEND"); // Not supported
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode BYTE = new(new[] { 8, 16, 16 }, 0x04, "BYTE");
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode ECI = new(null, 0x07, "ECI"); // character counts don't apply
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode KANJI = new(new[] { 8, 10, 12 }, 0x08, "KANJI");
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode FNC1_FIRST_POSITION = new(null, 0x05, "FNC1_FIRST_POSITION");
        /// <summary>
        /// 
        /// </summary>
        public static readonly Mode FNC1_SECOND_POSITION = new(null, 0x09, "FNC1_SECOND_POSITION");
        /// <summary>See GBT 18284-2000; "Hanzi" is a transliteration of this mode name.</summary>
        public static readonly Mode HANZI = new(new[] { 8, 10, 12 }, 0x0D, "HANZI");

        private readonly int[] characterCountBitsForVersions;
        private readonly int bits;
        private readonly string name;

        private Mode(int[] characterCountBitsForVersions, int bits, string name)
        {
            this.characterCountBitsForVersions = characterCountBitsForVersions;
            this.bits = bits;
            this.name = name;
        }

        /// <summary>
        /// Fors the bits.
        /// </summary>
        /// <param name="bits">four bits encoding a QR Code data mode</param>
        /// <returns>
        ///   <see cref="Mode"/> encoded by these bits
        /// </returns>
        /// <exception cref="ArgumentException">if bits do not correspond to a known mode</exception>
        public static Mode forBits(int bits) => bits switch
        {
            0x0 => TERMINATOR,
            0x1 => NUMERIC,
            0x2 => ALPHANUMERIC,
            0x3 => STRUCTURED_APPEND,
            0x4 => BYTE,
            0x5 => FNC1_FIRST_POSITION,
            0x7 => ECI,
            0x8 => KANJI,
            0x9 => FNC1_SECOND_POSITION,
            0xD =>
                // 0xD is defined in GBT 18284-2000, may not be supported in foreign country
                HANZI,
            _ => throw new ArgumentException()
        };

        /// <param name="version">version in question
        /// </param>
        /// <returns> number of bits used, in this QR Code symbol {@link Version}, to encode the
        /// count of characters that will follow encoded in this {@link Mode}
        /// </returns>
        public int getCharacterCountBits(Version version)
        {
            if (characterCountBitsForVersions == null)
            {
                throw new ArgumentException("Character count doesn't apply to this mode");
            }
            var number = version.VersionNumber;
            int offset = number switch
            {
                <= 9 => 0,
                <= 26 => 1,
                _ => 2
            };
            return characterCountBitsForVersions[offset];
        }

        /// <summary>
        /// Gets the bits.
        /// </summary>
        public int Bits
        {
            get => bits;
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/> that represents this instance.
        /// </returns>
        public override string ToString() => name;
    }
}