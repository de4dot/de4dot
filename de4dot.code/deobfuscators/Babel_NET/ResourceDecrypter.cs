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
using ICSharpCode.SharpZipLib.Zip.Compression;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class ResourceDecrypterCreator {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;

		public ResourceDecrypterCreator(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public ResourceDecrypter create() {
			return new ResourceDecrypter(module, simpleDeobfuscator);
		}
	}

	class ResourceDecrypter {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		MethodDefinition decryptMethod;
		IDecrypter decrypter;

		public ResourceDecrypter(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		interface IDecrypter {
			byte[] decrypt(byte[] encryptedData);
		}

		// v3.0
		class Decrypter1 : IDecrypter {
			ModuleDefinition module;

			public Decrypter1(ModuleDefinition module) {
				this.module = module;
			}

			public byte[] decrypt(byte[] encryptedData) {
				byte[] key, iv;
				var reader = new BinaryReader(new MemoryStream(encryptedData));
				bool isCompressed = getHeaderData(reader, out key, out iv);
				var data = DeobUtils.desDecrypt(encryptedData,
										(int)reader.BaseStream.Position,
										(int)(reader.BaseStream.Length - reader.BaseStream.Position),
										key, iv);
				if (isCompressed)
					data = DeobUtils.inflate(data, true);
				return data;
			}

			bool getHeaderData(BinaryReader reader, out byte[] key, out byte[] iv) {
				iv = reader.ReadBytes(reader.ReadByte());
				bool hasEmbeddedKey = reader.ReadBoolean();
				if (hasEmbeddedKey)
					key = reader.ReadBytes(reader.ReadByte());
				else {
					key = new byte[reader.ReadByte()];
					Array.Copy(module.Assembly.Name.PublicKey, 0, key, 0, key.Length);
				}

				reader.ReadBytes(reader.ReadInt32());	// hash
				return true;
			}
		}

		// v3.5+
		class Decrypter2 : IDecrypter {
			ModuleDefinition module;

			public Decrypter2(ModuleDefinition module) {
				this.module = module;
			}

			public byte[] decrypt(byte[] encryptedData) {
				int index = 0;
				byte[] key, iv;
				bool isCompressed = getKeyIv(getHeaderData(encryptedData, ref index), out key, out iv);
				var data = DeobUtils.desDecrypt(encryptedData, index, encryptedData.Length - index, key, iv);
				if (isCompressed)
					data = DeobUtils.inflate(data, true);
				return data;
			}

			byte[] getHeaderData(byte[] encryptedData, ref int index) {
				bool xorDecrypt = encryptedData[index++] != 0;
				var headerData = new byte[BitConverter.ToUInt16(encryptedData, index)];
				Array.Copy(encryptedData, index + 2, headerData, 0, headerData.Length);
				index += headerData.Length + 2;
				if (!xorDecrypt)
					return headerData;

				var key = new byte[8];
				Array.Copy(encryptedData, index, key, 0, key.Length);
				index += key.Length;
				for (int i = 0; i < headerData.Length; i++)
					headerData[i] ^= key[i % key.Length];
				return headerData;
			}

			bool getKeyIv(byte[] headerData, out byte[] key, out byte[] iv) {
				var reader = new BinaryReader(new MemoryStream(headerData));

				// 3.0 - 3.5 don't have this field
				if (headerData[(int)reader.BaseStream.Position] != 8) {
					var license = reader.ReadString();
				}

				// 4.2 (and earlier?) always compress the data
				bool isCompressed = true;
				if (headerData[(int)reader.BaseStream.Position] != 8)
					isCompressed = reader.ReadBoolean();

				iv = reader.ReadBytes(reader.ReadByte());
				bool hasEmbeddedKey = reader.ReadBoolean();
				if (hasEmbeddedKey)
					key = reader.ReadBytes(reader.ReadByte());
				else {
					key = new byte[reader.ReadByte()];
					Array.Copy(module.Assembly.Name.PublicKey, 12, key, 0, key.Length);
					key[5] |= 0x80;
				}
				return isCompressed;
			}
		}

		// v5.0+ retail
		class Decrypter3 : IDecrypter {
			ModuleDefinition module;
			Inflater inflater;

			public Decrypter3(ModuleDefinition module, MethodDefinition decryptMethod) {
				this.module = module;
				this.inflater = InflaterCreator.create(decryptMethod, true);
			}

			public byte[] decrypt(byte[] encryptedData) {
				int index = 0;
				byte[] key, iv;
				bool isCompressed = getKeyIv(getHeaderData(encryptedData, ref index), out key, out iv);
				var data = DeobUtils.desDecrypt(encryptedData, index, encryptedData.Length - index, key, iv);
				if (isCompressed)
					data = DeobUtils.inflate(data, inflater);
				return data;
			}

			byte[] getHeaderData(byte[] encryptedData, ref int index) {
				bool xorDecrypt = encryptedData[index++] != 0;
				var headerData = new byte[BitConverter.ToUInt16(encryptedData, index)];
				Array.Copy(encryptedData, index + 2, headerData, 0, headerData.Length);
				index += headerData.Length + 2;
				if (!xorDecrypt)
					return headerData;

				var key = new byte[6];
				Array.Copy(encryptedData, index, key, 0, key.Length);
				index += key.Length;
				for (int i = 0; i < headerData.Length; i++)
					headerData[i] ^= key[i % key.Length];
				return headerData;
			}

			bool getKeyIv(byte[] headerData, out byte[] key, out byte[] iv) {
				var reader = new BinaryReader(new MemoryStream(headerData));

				var license = reader.ReadString();
				bool isCompressed = reader.ReadBoolean();

				var unkData = reader.ReadBytes(reader.ReadInt32());

				bool hasEmbeddedKey = reader.ReadBoolean();

				iv = reader.ReadBytes(reader.ReadByte());
				if (hasEmbeddedKey)
					key = reader.ReadBytes(reader.ReadByte());
				else {
					key = new byte[reader.ReadByte()];
					Array.Copy(module.Assembly.Name.PublicKey, 12, key, 0, key.Length);
					key[5] |= 0x80;
				}
				return isCompressed;
			}
		}

		public MethodDefinition DecryptMethod {
			set {
				if (value == null)
					return;
				if (decryptMethod == null) {
					decryptMethod = value;
					simpleDeobfuscator.deobfuscate(decryptMethod);
				}
				else if (decryptMethod != value)
					throw new ApplicationException("Found another decrypter method");
			}
		}

		public static MethodDefinition findDecrypterMethod(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.Body == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.IO.MemoryStream", "(System.IO.Stream)"))
					continue;

				return calledMethod;
			}

			return null;
		}

		public byte[] decrypt(byte[] encryptedData) {
			if (decrypter == null)
				decrypter = createDecrypter(encryptedData);
			return decrypter.decrypt(encryptedData);
		}

		IDecrypter createDecrypter(byte[] encryptedData) {
			if (decryptMethod != null && DeobUtils.hasInteger(decryptMethod, 6))
				return new Decrypter3(module, decryptMethod);
			if (isV30(encryptedData))
				return new Decrypter1(module);
			return new Decrypter2(module);
		}

		static bool isV30(byte[] data) {
			return data.Length > 10 && data[0] == 8 && data[9] <= 1 && data[10] == 8;
		}
	}
}
