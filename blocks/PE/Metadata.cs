/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.IO;
using System.Text;

namespace de4dot.PE {
	public class Metadata : IFileLocation {
		uint magic;
		ushort majorVersion, minorVersion;
		uint reserved;
		string versionString;
		ushort flags;
		DotNetStream[] streams;

		uint offset, headerLength, length;

		public DotNetStream[] Streams {
			get { return streams; }
		}

		public uint Offset {
			get { return offset; }
		}

		public uint Length {
			get { return length; }
		}

		public uint HeaderLength {
			get { return headerLength; }
		}

		public uint HeaderEnd {
			get { return offset + headerLength; }
		}

		public Metadata(BinaryReader reader) {
			magic = reader.ReadUInt32();
			if (magic != 0x424A5342)
				return;

			offset = (uint)reader.BaseStream.Position - 4;
			majorVersion = reader.ReadUInt16();
			minorVersion = reader.ReadUInt16();
			reserved = reader.ReadUInt32();
			versionString = readString(reader, reader.ReadInt32());
			flags = reader.ReadUInt16();
			int numStreams = reader.ReadUInt16();
			streams = new DotNetStream[numStreams];
			uint lastOffset = offset;
			for (int i = 0; i < numStreams; i++) {
				uint fileOffset = offset + reader.ReadUInt32();
				uint size = reader.ReadUInt32();
				string name = readAsciizString(reader);
				streams[i] = new DotNetStream(name, fileOffset, size);
				lastOffset = Math.Max(lastOffset, fileOffset + size);
			}
			lastOffset = Math.Max(lastOffset, (uint)reader.BaseStream.Position);
			length = lastOffset - offset;
			headerLength = (uint)reader.BaseStream.Position - offset;
		}

		public DotNetStream getStream(string name) {
			foreach (var stream in streams) {
				if (stream.name == name)
					return stream;
			}
			return null;
		}

		string readString(BinaryReader reader, int len) {
			var sb = new StringBuilder(len);
			var nextPos = reader.BaseStream.Position + len;
			for (int i = 0; i < len; i++) {
				byte b = reader.ReadByte();
				if (b == 0)
					break;
				sb.Append((char)b);
			}
			reader.BaseStream.Position = nextPos;
			return sb.ToString();
		}

		string readAsciizString(BinaryReader reader) {
			var sb = new StringBuilder();
			while (true) {
				byte b = reader.ReadByte();
				if (b == 0)
					break;
				sb.Append((char)b);
			}
			reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
			return sb.ToString();
		}
	}
}
