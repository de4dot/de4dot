/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
using dnlib.IO;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ErexResourceReader {
		DataReader reader;
		uint[] key;

		public ErexResourceReader(ref DataReader reader) => this.reader = reader;

		public byte[] Decrypt() {
			if (reader.ReadUInt32() != 0x58455245)
				throw new InvalidDataException("Invalid EREX sig");
			if (reader.ReadInt32() > 1)
				throw new ApplicationException("Invalid EREX file");

			byte flags = reader.ReadByte();
			bool isEncrypted = (flags & 1) != 0;
			bool isDeflated = (flags & 2) != 0;

			int length = reader.ReadInt32();
			if (length < 0)
				throw new ApplicationException("Invalid length");

			if (isEncrypted)
				ReadKey();

			if (isDeflated)
				reader = Inflate(length);

			if (isEncrypted)
				reader = Decrypt(length);

			return reader.ReadBytes(length);
		}

		void ReadKey() {
			key = new uint[reader.ReadByte()];
			for (int i = 0; i < key.Length; i++)
				key[i] = reader.ReadUInt32();
		}

		DataReader Inflate(int length) {
			var data = reader.ReadRemainingBytes();
			return ByteArrayDataReaderFactory.CreateReader(DeobUtils.Inflate(data, true));
		}

		DataReader Decrypt(int length) {
			var block = new uint[4];
			var decrypted = new byte[16];

			var outStream = new MemoryStream(length);
			while (reader.Position < reader.Length) {
				block[0] = reader.ReadUInt32();
				block[1] = reader.ReadUInt32();
				block[2] = reader.ReadUInt32();
				block[3] = reader.ReadUInt32();
				DeobUtils.XxteaDecrypt(block, key);
				Buffer.BlockCopy(block, 0, decrypted, 0, decrypted.Length);
				outStream.Write(decrypted, 0, decrypted.Length);
			}

			return ByteArrayDataReaderFactory.CreateReader(outStream.ToArray());
		}
	}
}
