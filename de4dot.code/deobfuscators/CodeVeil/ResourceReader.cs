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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ResourceReader {
		BinaryReader reader;
		string resourceReader;
		string resourceSet;

		public string ResourceReaderName {
			get { return resourceReader; }
		}

		public string ResourceSetName {
			get { return resourceSet; }
		}

		public ResourceReader(Stream stream) {
			stream.Position = 0;
			reader = new BinaryReader(stream);
		}

		public ResourceReader(BinaryReader reader) {
			this.reader = reader;
		}

		public ResourceInfo[] read() {
			if (reader.ReadUInt32() != 0xBEEFCACE)
				throw new InvalidDataException("Invalid magic");
			if (reader.ReadUInt32() <= 0)
				throw new InvalidDataException("Invalid number");
			reader.ReadUInt32();
			resourceReader = reader.ReadString();
			if (Utils.StartsWith(resourceReader, "System.Resources.ResourceReader", StringComparison.Ordinal))
				throw new InvalidDataException("Resource isn't encrypted");
			resourceSet = reader.ReadString();
			if (reader.ReadByte() != 1)
				throw new ApplicationException("Invalid version");

			int flags = reader.ReadByte();
			if ((flags & 0xFC) != 0)
				throw new ApplicationException("Invalid flags");
			bool inflateData = (flags & 1) != 0;
			bool encrypted = (flags & 2) != 0;

			int numResources = reader.ReadInt32();
			if (numResources < 0)
				throw new ApplicationException("Invalid number of resources");

			var infos = new ResourceInfo[numResources];
			for (int i = 0; i < numResources; i++) {
				var resourceName = readResourceName(reader, encrypted);
				int offset = reader.ReadInt32();
				byte resourceFlags = reader.ReadByte();
				int resourceLength = (resourceFlags & 0x80) == 0 ? -1 : reader.ReadInt32();
				infos[i] = new ResourceInfo(resourceName, resourceFlags, offset, resourceLength);
			}

			var dataReader = reader;
			if (encrypted) {
				var key = new uint[4];
				key[0] = dataReader.ReadUInt32();
				key[1] = dataReader.ReadUInt32();
				int numDwords = dataReader.ReadInt32();
				if (numDwords < 0 || numDwords >= 0x40000000)
					throw new ApplicationException("Invalid number of encrypted dwords");
				var encryptedData = new uint[numDwords];
				for (int i = 0; i < numDwords; i++)
					encryptedData[i] = dataReader.ReadUInt32();
				key[2] = dataReader.ReadUInt32();
				key[3] = dataReader.ReadUInt32();
				DeobUtils.xxteaDecrypt(encryptedData, key);
				byte[] decryptedData = new byte[encryptedData.Length * 4];
				Buffer.BlockCopy(encryptedData, 0, decryptedData, 0, decryptedData.Length);
				dataReader = new BinaryReader(new MemoryStream(decryptedData));
			}

			if (inflateData) {
				var data = dataReader.ReadBytes((int)(dataReader.BaseStream.Length - dataReader.BaseStream.Position));
				data = DeobUtils.inflate(data, true);
				dataReader = new BinaryReader(new MemoryStream(data));
			}

			foreach (var info in infos)
				info.dataReader = dataReader;

			return infos;
		}

		static string readResourceName(BinaryReader reader, bool encrypted) {
			if (!encrypted)
				return reader.ReadString();

			int len = reader.ReadInt32();
			if (len < 0)
				throw new ApplicationException("Invalid string length");
			var sb = new StringBuilder(len);
			for (int i = 0; i < len; i++)
				sb.Append((char)rol3(reader.ReadChar()));
			return sb.ToString();
		}

		static char rol3(char c) {
			ushort s = (ushort)c;
			return (char)((s << 3) | (s >> (16 - 3)));
		}
	}
}
