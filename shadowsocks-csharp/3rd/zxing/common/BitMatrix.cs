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

using System;

namespace ZXing.Common;

/// <summary>
///   <p>Represents a 2D matrix of bits. In function arguments below, and throughout the common
/// module, x is the column position, and y is the row position. The ordering is always x, y.
/// The origin is at the top-left.</p>
///   <p>Internally the bits are represented in a 1-D array of 32-bit ints. However, each row begins
/// with a new int. This is done intentionally so that we can copy out a row into a BitArray very
/// efficiently.</p>
///   <p>The ordering of bits is row-major. Within each int, the least significant bits are used first,
/// meaning they represent lower x values. This is compatible with BitArray's implementation.</p>
/// </summary>
/// <author>Sean Owen</author>
/// <author>dswitkin@google.com (Daniel Switkin)</author>
public sealed partial class BitMatrix
{
   private readonly int width;
   private readonly int height;
   private readonly int rowSize;
   private readonly int[] bits;

   /// <returns> The width of the matrix
   /// </returns>
   public int Width
   {
      get => width;
   }
   /// <returns> The height of the matrix
   /// </returns>
   public int Height
   {
      get => height;
   }

   // A helper to construct a square matrix.
   public BitMatrix(int dimension)
      : this(dimension, dimension)
   {
   }

   public BitMatrix(int width, int height)
   {
      if (width < 1 || height < 1)
      {
         throw new ArgumentException("Both dimensions must be greater than 0");
      }
      this.width = width;
      this.height = height;
      rowSize = width + 31 >> 5;
      bits = new int[rowSize * height];
   }

   private BitMatrix(int width, int height, int rowSize, int[] bits)
   {
      this.width = width;
      this.height = height;
      this.rowSize = rowSize;
      this.bits = bits;
   }

   /// <summary> <p>Gets the requested bit, where true means black.</p>
   /// 
   /// </summary>
   /// <param name="x">The horizontal component (i.e. which column)
   /// </param>
   /// <param name="y">The vertical component (i.e. which row)
   /// </param>
   /// <returns> value of given bit in matrix
   /// </returns>
   public bool this[int x, int y]
   {
      get
      {
         var offset = y * rowSize + (x >> 5);
         return ((int)((uint)bits[offset] >> (x & 0x1f)) & 1) != 0;
      }
      set
      {
         if (value)
         {
            var offset = y * rowSize + (x >> 5);
            bits[offset] |= 1 << (x & 0x1f);
         }
      }
   }

   /// <summary> <p>Flips the given bit.</p>
   /// 
   /// </summary>
   /// <param name="x">The horizontal component (i.e. which column)
   /// </param>
   /// <param name="y">The vertical component (i.e. which row)
   /// </param>
   public void flip(int x, int y)
   {
      var offset = y * rowSize + (x >> 5);
      bits[offset] ^= 1 << (x & 0x1f);
   }


   /// <summary> <p>Sets a square region of the bit matrix to true.</p>
   /// 
   /// </summary>
   /// <param name="left">The horizontal position to begin at (inclusive)
   /// </param>
   /// <param name="top">The vertical position to begin at (inclusive)
   /// </param>
   /// <param name="width">The width of the region
   /// </param>
   /// <param name="height">The height of the region
   /// </param>
   public void setRegion(int left, int top, int width, int height)
   {
      if (top < 0 || left < 0)
      {
         throw new ArgumentException("Left and top must be nonnegative");
      }
      if (height < 1 || width < 1)
      {
         throw new ArgumentException("Height and width must be at least 1");
      }
      var right = left + width;
      var bottom = top + height;
      if (bottom > this.height || right > this.width)
      {
         throw new ArgumentException("The region must fit inside the matrix");
      }
      for (var y = top; y < bottom; y++)
      {
         var offset = y * rowSize;
         for (var x = left; x < right; x++)
         {
            bits[offset + (x >> 5)] |= 1 << (x & 0x1f);
         }
      }
   }

   /// <summary> A fast method to retrieve one row of data from the matrix as a BitArray.
   /// 
   /// </summary>
   /// <param name="y">The row to retrieve
   /// </param>
   /// <param name="row">An optional caller-allocated BitArray, will be allocated if null or too small
   /// </param>
   /// <returns> The resulting BitArray - this reference should always be used even when passing
   /// your own row
   /// </returns>
   public BitArray getRow(int y, BitArray row)
   {
      if (row == null || row.Size < width)
      {
         row = new BitArray(width);
      }
      else
      {
         row.clear();
      }
      var offset = y * rowSize;
      for (var x = 0; x < rowSize; x++)
      {
         row.setBulk(x << 5, bits[offset + x]);
      }
      return row;
   }

   /// <summary>
   /// Sets the row.
   /// </summary>
   /// <param name="y">row to set</param>
   /// <param name="row">{@link BitArray} to copy from</param>
   public void setRow(int y, BitArray row)
   {
      Array.Copy(row.Array, 0, bits, y * rowSize, rowSize);
   }


   /// <summary>
   /// This is useful in detecting a corner of a 'pure' barcode.
   /// </summary>
   /// <returns>{x,y} coordinate of top-left-most 1 bit, or null if it is all white</returns>
   public int[] getTopLeftOnBit()
   {
      var bitsOffset = 0;
      while (bitsOffset < bits.Length && bits[bitsOffset] == 0)
      {
         bitsOffset++;
      }
      if (bitsOffset == bits.Length)
      {
         return null;
      }
      var y = bitsOffset / rowSize;
      var x = bitsOffset % rowSize << 5;

      var theBits = bits[bitsOffset];
      var bit = 0;
      while (theBits << 31 - bit == 0)
      {
         bit++;
      }
      x += bit;
      return new[] { x, y };
   }

   public int[] getBottomRightOnBit()
   {
      var bitsOffset = bits.Length - 1;
      while (bitsOffset >= 0 && bits[bitsOffset] == 0)
      {
         bitsOffset--;
      }
      if (bitsOffset < 0)
      {
         return null;
      }

      var y = bitsOffset / rowSize;
      var x = bitsOffset % rowSize << 5;

      var theBits = bits[bitsOffset];
      var bit = 31;

      while ((int)((uint)theBits >> bit) == 0) // (theBits >>> bit)
      {
         bit--;
      }
      x += bit;

      return new[] { x, y };
   }

}