﻿/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace Lucene.Net.Util
{
    using DocIdSetIterator = Lucene.Net.Search.DocIdSetIterator;

    /// <summary>
    /// An iterator to iterate over set bits in an OpenBitSet.
    /// this is faster than nextSetBit() for iterating over the complete set of bits,
    /// especially when the density of the bits set is high.
    /// </summary>
    public class OpenBitSetIterator : DocIdSetIterator
    {
        // hmmm, what about an iterator that finds zeros though,
        // or a reverse iterator... should they be separate classes
        // for efficiency, or have a common root interface?  (or
        // maybe both?  could ask for a SetBitsIterator, etc...

        private readonly long[] array;
        private readonly int words;
        private int position = -1;
        private long word;
        private int wordShift;
        private int indexArray;
        private int currentDocId = -1;

        public OpenBitSetIterator(OpenBitSet obs)
            : this(obs.Bits, obs.NumWords)
        {
        }

        public OpenBitSetIterator(long[] bits, int numWords)
        {
            array = bits;
            words = numWords;
        }

        // 64 bit shifts
        private void Shift()
        {
            if ((int)word == 0)
            {
                wordShift += 32;
                word = (long)((ulong)word >> 32);
            }
            if ((word & 0x0000FFFF) == 0)
            {
                wordShift += 16;
                word = (long)((ulong)word >> 16);
            }
            if ((word & 0x000000FF) == 0)
            {
                wordShift += 8;
                word = (long)((ulong)word >> 8);
            }
            indexArray = BitUtil.BitList((byte)word);
        }

        /// <summary>
        ///*** alternate shift implementations
        /// // 32 bit shifts, but a long shift needed at the end
        /// private void shift2() {
        ///  int y = (int)word;
        ///  if (y==0) {wordShift +=32; y = (int)(word >>>32); }
        ///  if ((y & 0x0000FFFF) == 0) { wordShift +=16; y>>>=16; }
        ///  if ((y & 0x000000FF) == 0) { wordShift +=8; y>>>=8; }
        ///  indexArray = bitlist[y & 0xff];
        ///  word >>>= (wordShift +1);
        /// }
        ///
        /// private void shift3() {
        ///  int lower = (int)word;
        ///  int lowByte = lower & 0xff;
        ///  if (lowByte != 0) {
        ///    indexArray=bitlist[lowByte];
        ///    return;
        ///  }
        ///  shift();
        /// }
        /// *****
        /// </summary>

        public override int NextDoc()
        {
            if (indexArray == 0)
            {
                if (word != 0)
                {
                    word = (long)((ulong)word >> 8);
                    wordShift += 8;
                }

                while (word == 0)
                {
                    if (++position >= words)
                    {
                        return currentDocId = NO_MORE_DOCS;
                    }
                    word = array[position];
                    wordShift = -1; // loop invariant code motion should move this
                }

                // after the first time, should I go with a linear search, or
                // stick with the binary search in shift?
                Shift();
            }

            int bitIndex = (indexArray & 0x0f) + wordShift;
            indexArray = (int)((uint)indexArray >> 4);
            // should i<<6 be cached as a separate variable?
            // it would only save one cycle in the best circumstances.
            return currentDocId = (position << 6) + bitIndex;
        }

        public override int Advance(int target)
        {
            indexArray = 0;
            position = target >> 6;
            if (position >= words)
            {
                word = 0; // setup so next() will also return -1
                return currentDocId = NO_MORE_DOCS;
            }
            wordShift = target & 0x3f;
            word = (long)((ulong)array[position] >> wordShift);
            if (word != 0)
            {
                wordShift--; // compensate for 1 based arrIndex
            }
            else
            {
                while (word == 0)
                {
                    if (++position >= words)
                    {
                        return currentDocId = NO_MORE_DOCS;
                    }
                    word = array[position];
                }
                wordShift = -1;
            }

            Shift();

            int bitIndex = (indexArray & 0x0f) + wordShift;
            indexArray = (int)((uint)indexArray >> 4);
            // should i<<6 be cached as a separate variable?
            // it would only save one cycle in the best circumstances.
            return currentDocId = (position << 6) + bitIndex;
        }

        public override int DocId
        {
            get { return this.currentDocId; }
           
        }

        public override long Cost()
        {
            return words / 64;
        }

        protected override void Reset()
        {
            this.position = -1;
            this.currentDocId = -1;
        }
    }
}