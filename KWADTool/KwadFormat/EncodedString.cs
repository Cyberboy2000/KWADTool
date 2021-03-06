﻿using System.IO;

namespace KWADTool.KwadFormat
{
    public class EncodedString
    {
        public uint Length { get; private set; }
        private readonly char[] charArray;

        public char[] GetCharArray()
        {
            return (char[]) charArray.Clone();
        }

        public EncodedString(BinaryReader reader)
        {
            Length = reader.ReadUInt32();
            charArray = reader.ReadChars((int) (Length + (4 - Length % 4) % 4));
        }

        public string GetString()
        {
            return new string(charArray, 0, (int) Length);
        }
    }
}