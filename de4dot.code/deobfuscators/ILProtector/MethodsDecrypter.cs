/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.ILProtector {
	class MethodsDecrypter {
		ModuleDefMD module;
		MainType mainType;
		EmbeddedResource methodsResource;
		Dictionary<int, MethodInfo2> methodInfos = new Dictionary<int, MethodInfo2>();
		List<TypeDef> delegateTypes = new List<TypeDef>();
		IDecrypter decrypter;

		interface IDecrypter {
			string Version { get; }
			byte[] getMethodsData(EmbeddedResource resource);
		}

		class DecrypterBase : IDecrypter {
			protected static readonly byte[] ilpPublicKeyToken = new byte[8] { 0x20, 0x12, 0xD3, 0xC0, 0x55, 0x1F, 0xE0, 0x3D };

			protected string ilpVersion;
			protected int startOffset;
			protected byte[] decryptionKey;
			protected int decryptionKeyMod;

			public string Version {
				get { return ilpVersion; }
			}

			protected void setVersion(Version version) {
				if (version.Revision == 0)
					ilpVersion = string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
				else
					ilpVersion = version.ToString();
			}

			public virtual byte[] getMethodsData(EmbeddedResource resource) {
				var reader = resource.Data;
				reader.Position = startOffset;
				if ((reader.ReadInt32() & 1) != 0)
					return decompress(reader);
				else
					return reader.ReadRemainingBytes();
			}

			byte[] decompress(IBinaryReader reader) {
				return decompress(reader, decryptionKey, decryptionKeyMod);
			}

			static void copy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int size) {
				for (int i = 0; i < size; i++)
					dst[dstIndex++] = src[srcIndex++];
			}

			static byte[] decompress(IBinaryReader reader, byte[] key, int keyMod) {
				return decompress(new byte[reader.Read7BitEncodedUInt32()], reader, key, keyMod);
			}

			protected static byte[] decompress(byte[] decrypted, IBinaryReader reader, byte[] key, int keyMod) {
				int destIndex = 0;
				while (reader.Position < reader.Length) {
					if (destIndex >= decrypted.Length)
						break;
					byte flags = reader.ReadByte();
					for (int mask = 1; mask != 0x100; mask <<= 1) {
						if (reader.Position >= reader.Length)
							break;
						if (destIndex >= decrypted.Length)
							break;
						if ((flags & mask) != 0) {
							int displ = (int)reader.Read7BitEncodedUInt32();
							int size = (int)reader.Read7BitEncodedUInt32();
							copy(decrypted, destIndex - displ, decrypted, destIndex, size);
							destIndex += size;
						}
						else {
							byte b = reader.ReadByte();
							if (key != null)
								b ^= key[destIndex % keyMod];
							decrypted[destIndex++] = b;
						}
					}
				}

				return decrypted;
			}
		}

		// 1.0.0 - 1.0.4
		class DecrypterV100 : DecrypterBase {
			// This is the first four bytes of ILProtector's public key token
			const uint RESOURCE_MAGIC = 0xC0D31220;

			DecrypterV100(Version ilpVersion) {
				setVersion(ilpVersion);
				this.startOffset = 8;
				this.decryptionKey = ilpPublicKeyToken;
				this.decryptionKeyMod = 8;
			}

			public static DecrypterV100 create(IBinaryReader reader) {
				reader.Position = 0;
				if (reader.Length < 12)
					return null;
				if (reader.ReadUInt32() != RESOURCE_MAGIC)
					return null;

				return new DecrypterV100(new Version(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()));
			}
		}

		// 1.0.5
		class DecrypterV105 : DecrypterBase {
			DecrypterV105(Version ilpVersion, byte[] key) {
				setVersion(ilpVersion);
				this.startOffset = 0xA0;
				this.decryptionKey = key;
				this.decryptionKeyMod = key.Length - 1;
			}

			public static DecrypterV105 create(IBinaryReader reader) {
				reader.Position = 0;
				if (reader.Length < 0xA4)
					return null;
				var key = reader.ReadBytes(0x94);
				if (!Utils.compare(reader.ReadBytes(8), ilpPublicKeyToken))
					return null;
				return new DecrypterV105(new Version(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()), key);
			}
		}

		// 1.0.6
		class DecrypterV106 : DecrypterBase {
			byte[] decryptionKey6;
			byte[] decryptionKey7;

			DecrypterV106(byte[] key0, byte[] key6, byte[] key7, int startOffset) {
				this.ilpVersion = "1.0.6";
				this.startOffset = startOffset;
				this.decryptionKey = key0;
				this.decryptionKey6 = key6;
				this.decryptionKey7 = key7;
				this.decryptionKeyMod = key0.Length - 1;
			}

			public static DecrypterV106 create(IBinaryReader reader) {
				try {
					int keyXorOffs7 = (ReadByteAt(reader, 0) ^ ReadByteAt(reader, 2)) + 2;
					reader.Position = keyXorOffs7 + (ReadByteAt(reader, 1) ^ ReadByteAt(reader, keyXorOffs7));

					int sha1DataLen = reader.Read7BitEncodedInt32() + 0x80;
					int keyXorOffs6 = (int)reader.Position;
					int encryptedOffs = (int)reader.Position + sha1DataLen;
					var sha1Data = reader.ReadBytes(sha1DataLen);
					uint crc32 = CRC32.checksum(sha1Data);

					reader.Position = reader.Length - 0x18;
					uint origCrc32 = reader.ReadUInt32();
					if (crc32 != origCrc32)
						return null;

					var key0 = DeobUtils.sha1Sum(sha1Data);			// 1.0.6.0
					var key6 = getKey(reader, key0, keyXorOffs6);	// 1.0.6.6
					var key7 = getKey(reader, key0, keyXorOffs7);	// 1.0.6.7
					return new DecrypterV106(key0, key6, key7, encryptedOffs);
				}
				catch (IOException) {
					return null;
				}
			}

			static byte[] getKey(IBinaryReader reader, byte[] sha1Sum, int offs) {
				var key = (byte[])sha1Sum.Clone();
				reader.Position = offs;
				for (int i = 0; i < key.Length; i++)
					key[i] ^= reader.ReadByte();
				return key;
			}

			static byte ReadByteAt(IBinaryReader reader, int offs) {
				reader.Position = offs;
				return reader.ReadByte();
			}

			public override byte[] getMethodsData(EmbeddedResource resource) {
				var reader = resource.Data;
				reader.Position = startOffset;
				var decrypted = new byte[reader.Read7BitEncodedUInt32()];
				uint origCrc32 = reader.ReadUInt32();
				long pos = reader.Position;

				var keys = new byte[][] { decryptionKey, decryptionKey6, decryptionKey7 };
				foreach (var key in keys) {
					try {
						reader.Position = pos;
						decompress(decrypted, reader, key, decryptionKeyMod);
						uint crc32 = CRC32.checksum(decrypted);
						if (crc32 == origCrc32)
							return decrypted;
					}
					catch (OutOfMemoryException) {
					}
					catch (IOException) {
					}
				}

				throw new ApplicationException("Could not decrypt methods data");
			}
		}

		class MethodInfo2 {
			public int id;
			public int offset;
			public byte[] data;
			public MethodInfo2(int id, int offset, int size) {
				this.id = id;
				this.offset = offset;
				this.data = new byte[size];
			}

			public override string ToString() {
				return string.Format("{0} {1:X8} 0x{2:X}", id, offset, data.Length);
			}
		}

		public EmbeddedResource Resource {
			get { return methodsResource; }
		}

		public IEnumerable<TypeDef> DelegateTypes {
			get { return delegateTypes; }
		}

		public string Version {
			get { return decrypter == null ? null : decrypter.Version; }
		}

		public bool Detected {
			get { return methodsResource != null; }
		}

		public MethodsDecrypter(ModuleDefMD module, MainType mainType) {
			this.module = module;
			this.mainType = mainType;
		}

		public void find() {
			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				var reader = resource.Data;
				reader.Position = 0;
				if (!checkResourceV100(reader) &&
					!checkResourceV105(reader) &&
					!checkResourceV106(reader))
					continue;

				methodsResource = resource;
				break;
			}
		}

		bool checkResourceV100(IBinaryReader reader) {
			decrypter = DecrypterV100.create(reader);
			return decrypter != null;
		}

		bool checkResourceV105(IBinaryReader reader) {
			decrypter = DecrypterV105.create(reader);
			return decrypter != null;
		}

		bool checkResourceV106(IBinaryReader reader) {
			decrypter = DecrypterV106.create(reader);
			return decrypter != null;
		}

		public void decrypt() {
			if (methodsResource == null || decrypter == null)
				return;

			foreach (var info in readMethodInfos(decrypter.getMethodsData(methodsResource)))
				methodInfos[info.id] = info;

			restoreMethods();
		}

		static MethodInfo2[] readMethodInfos(byte[] data) {
			var reader = MemoryImageStream.Create(data);
			int numMethods = (int)reader.Read7BitEncodedUInt32();
			int totalCodeSize = (int)reader.Read7BitEncodedUInt32();
			var methodInfos = new MethodInfo2[numMethods];
			int offset = 0;
			for (int i = 0; i < numMethods; i++) {
				int id = (int)reader.Read7BitEncodedUInt32();
				int size = (int)reader.Read7BitEncodedUInt32();
				methodInfos[i] = new MethodInfo2(id, offset, size);
				offset += size;
			}
			long dataOffset = reader.Position;
			foreach (var info in methodInfos) {
				reader.Position = dataOffset + info.offset;
				reader.Read(info.data, 0, info.data.Length);
			}
			return methodInfos;
		}

		void restoreMethods() {
			if (methodInfos.Count == 0)
				return;

			Logger.v("Restoring {0} methods", methodInfos.Count);
			Logger.Instance.indent();
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method.Body == null)
						continue;

					if (restoreMethod(method)) {
						Logger.v("Restored method {0} ({1:X8}). Instrs:{2}, Locals:{3}, Exceptions:{4}",
							Utils.removeNewlines(method.FullName),
							method.MDToken.ToInt32(),
							method.Body.Instructions.Count,
							method.Body.Variables.Count,
							method.Body.ExceptionHandlers.Count);
					}
				}
			}
			Logger.Instance.deIndent();
			if (methodInfos.Count != 0)
				Logger.w("{0} methods weren't restored", methodInfos.Count);
		}

		const int INVALID_METHOD_ID = -1;
		bool restoreMethod(MethodDef method) {
			int methodId = getMethodId(method);
			if (methodId == INVALID_METHOD_ID)
				return false;

			var parameters = method.Parameters;
			var methodInfo = methodInfos[methodId];
			methodInfos.Remove(methodId);
			var methodReader = new MethodReader(module, methodInfo.data, parameters);
			methodReader.read();

			restoreMethod(method, methodReader);
			delegateTypes.Add(methodReader.DelegateType);

			return true;
		}

		static void restoreMethod(MethodDef method, MethodReader methodReader) {
			// body.MaxStackSize = <let dnlib calculate this>
			method.Body.InitLocals = methodReader.InitLocals;
			methodReader.RestoreMethod(method);
		}

		int getMethodId(MethodDef method) {
			if (method == null || method.Body == null)
				return INVALID_METHOD_ID;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldsfld = instrs[i];
				if (ldsfld.OpCode.Code != Code.Ldsfld)
					continue;

				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;

				var field = ldsfld.Operand as FieldDef;
				if (field == null || field != mainType.InvokerInstanceField)
					continue;

				return ldci4.GetLdcI4Value();
			}

			return INVALID_METHOD_ID;
		}
	}
}
