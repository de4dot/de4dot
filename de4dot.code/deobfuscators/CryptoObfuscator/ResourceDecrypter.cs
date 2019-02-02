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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class ResourceDecrypter {
		const int BUFLEN = 0x8000;
		ModuleDefMD module;
		TypeDef resourceDecrypterType;
		byte[] buffer1 = new byte[BUFLEN];
		byte[] buffer2 = new byte[BUFLEN];
		byte desEncryptedFlag;
		byte deflatedFlag;
		byte bitwiseNotEncryptedFlag;
		FrameworkType frameworkType;
		bool flipFlagsBits;
		bool skipBeforeFlag;
		int skipBytes;

		public ResourceDecrypter(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			frameworkType = DotNetUtils.GetFrameworkType(module);
			Find(simpleDeobfuscator);
		}

		void Find(ISimpleDeobfuscator simpleDeobfuscator) {
			switch (frameworkType) {
			case FrameworkType.Silverlight:
				FindSilverlight();
				break;

			case FrameworkType.Desktop:
			case FrameworkType.CompactFramework:
				if (!module.IsClr1x) {
					if (FindDesktopOrCompactFramework())
						break;
				}
				FindDesktopOrCompactFrameworkV1();
				break;
			}

			InitializeHeaderInfo(simpleDeobfuscator);
		}

		static string[] requiredTypes = new string[] {
			"System.IO.MemoryStream",
			"System.Object",
			"System.Int32",
		};
		bool FindDesktopOrCompactFramework() {
			resourceDecrypterType = null;
			foreach (var type in module.Types) {
				if (type.Fields.Count < 5)
					continue;
				if (!new FieldTypes(type).All(requiredTypes))
					continue;

				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;

				if (!CheckCctor(cctor))
					continue;

				resourceDecrypterType = type;
				return true;
			}
			return false;
		}

		bool CheckCctor(MethodDef cctor) {
			if (cctor.Body == null)
				return false;
			int stsfldCount = 0;
			foreach (var instr in cctor.Body.Instructions) {
				if (instr.OpCode.Code == Code.Stsfld) {
					var field = instr.Operand as IField;
					if (!new SigComparer().Equals(cctor.DeclaringType, field.DeclaringType))
						return false;
					stsfldCount++;
				}
			}
			return stsfldCount >= cctor.DeclaringType.Fields.Count;
		}

		static string[] requiredLocals_v1 = new string[] {
			"System.Boolean",
			"System.Byte",
			"System.Byte[]",
			"System.Int32",
			"System.Security.Cryptography.DESCryptoServiceProvider",
		};
		bool FindDesktopOrCompactFrameworkV1() {
			resourceDecrypterType = null;
			foreach (var type in module.Types) {
				if (type.Fields.Count != 0)
					continue;

				foreach (var method in GetDecrypterMethods(type)) {
					if (method == null)
						continue;
					if (!new LocalTypes(method).Exactly(requiredLocals_v1))
						continue;
					if (!DotNetUtils.CallsMethod(method, "System.Int64", "()"))
						continue;
					if (!DotNetUtils.CallsMethod(method, "System.Int32", "(System.Byte[],System.Int32,System.Int32)"))
						continue;
					if (!DotNetUtils.CallsMethod(method, "System.Void", "(System.Array,System.Int32,System.Array,System.Int32,System.Int32)"))
						continue;
					if (!DotNetUtils.CallsMethod(method, "System.Security.Cryptography.ICryptoTransform", "()"))
						continue;
					if (!DotNetUtils.CallsMethod(method, "System.Byte[]", "(System.Byte[],System.Int32,System.Int32)"))
						continue;

					resourceDecrypterType = type;
					return true;
				}
			}
			return false;
		}

		static string[] requiredLocals_sl = new string[] {
			"System.Byte",
			"System.Byte[]",
			"System.Int32",
		};
		void FindSilverlight() {
			foreach (var type in module.Types) {
				if (type.Fields.Count > 0)
					continue;
				if (type.HasNestedTypes || type.HasGenericParameters)
					continue;

				foreach (var method in GetDecrypterMethods(type)) {
					if (method == null)
						continue;
					if (!new LocalTypes(method).All(requiredLocals_sl))
						continue;

					resourceDecrypterType = type;
					return;
				}
			}
		}

		void InitializeHeaderInfo(ISimpleDeobfuscator simpleDeobfuscator) {
			skipBytes = 0;

			foreach (var method in GetDecrypterMethods(resourceDecrypterType)) {
				if (UpdateFlags(method, simpleDeobfuscator))
					return;
			}

			desEncryptedFlag = 1;
			deflatedFlag = 2;
			bitwiseNotEncryptedFlag = 4;
		}

		static bool CheckFlipBits(MethodDef method, out int index) {
			int nots = 0, i;
			var instrs = method.Body.Instructions;
			index = -1;
			for (i = 0; i < instrs.Count - 1; i++) {
				var ldloc = instrs[i];
				if (!ldloc.IsLdloc())
					continue;
				var local = ldloc.GetLocal(method.Body.Variables);
				if (local == null || local.Type.GetElementType().GetPrimitiveSize() < 0)
					continue;
				if (instrs[i + 1].OpCode.Code == Code.Not) {
					nots++;
					index = i + 1;
				}
			}
			return (nots & 1) == 1;
		}

		bool UpdateFlags(MethodDef method, ISimpleDeobfuscator simpleDeobfuscator) {
			if (method == null || method.Body == null || method.Body.Variables.Count < 3)
				return false;

			var constants = new List<int>();
			simpleDeobfuscator.Deobfuscate(method);
			var instructions = method.Body.Instructions;
			for (int i = 2; i < instructions.Count; i++) {
				var and = instructions[i];
				if (and.OpCode.Code != Code.And)
					continue;
				var ldci4 = instructions[i - 1];
				if (!ldci4.IsLdcI4())
					continue;
				int flagValue = ldci4.GetLdcI4Value();
				if (!IsFlag(flagValue))
					continue;
				var ldloc = instructions[i - 2];
				if (!ldloc.IsLdloc())
					continue;
				var local = ldloc.GetLocal(method.Body.Variables);
				if (local.Type.GetElementType().GetPrimitiveSize() < 0)
					continue;
				constants.Add(flagValue);
			}

			flipFlagsBits = CheckFlipBits(method, out int notIndex);
			skipBytes = GetHeaderSkipBytes(method, out int skipIndex);
			skipBeforeFlag = skipIndex < notIndex;

			switch (frameworkType) {
			case FrameworkType.Desktop:
				if (!module.IsClr1x) {
					if (constants.Count == 2) {
						desEncryptedFlag = (byte)constants[0];
						deflatedFlag = (byte)constants[1];
						return true;
					}
				}
				if (constants.Count == 1) {
					desEncryptedFlag = (byte)constants[0];
					return true;
				}
				break;

			case FrameworkType.Silverlight:
				if (constants.Count == 1) {
					bitwiseNotEncryptedFlag = (byte)constants[0];
					return true;
				}
				break;

			case FrameworkType.CompactFramework:
				if (constants.Count == 1) {
					desEncryptedFlag = (byte)constants[0];
					return true;
				}
				break;
			}

			return false;
		}

		static int GetHeaderSkipBytes(MethodDef method, out int index) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4())
					continue;
				int loopCount = ldci4.GetLdcI4Value();
				if (loopCount < 2 || loopCount > 4)
					continue;
				var blt = instrs[i + 1];
				if (blt.OpCode.Code != Code.Blt && blt.OpCode.Code != Code.Blt_S && blt.OpCode.Code != Code.Clt)
					continue;
				index = i;
				return loopCount - 1;
			}
			index = 0;
			return 0;
		}

		static bool IsFlag(int value) {
			for (uint tmp = (uint)value; tmp != 0; tmp >>= 1) {
				if ((tmp & 1) != 0)
					return tmp == 1;
			}
			return false;
		}

		static IEnumerable<MethodDef> GetDecrypterMethods(TypeDef type) {
			if (type == null)
				yield break;
			foreach (var method in type.Methods) {
				if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.IO.Stream)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Int64,System.IO.Stream)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Int64,System.IO.Stream,System.UInt32)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Int32,System.IO.Stream)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Int16,System.IO.Stream)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Byte,System.IO.Stream)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.SByte,System.IO.Stream)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Byte,System.IO.Stream,System.Int32)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.SByte,System.IO.Stream,System.UInt32)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Char,System.IO.Stream)"))
					yield return method;
				else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Int64,System.Object)"))
					yield return method;
			}
		}

		public byte[] Decrypt(Stream resourceStream) {
			var sourceStream = resourceStream;
			int sourceStreamOffset = 1;
			bool didSomething = false;

			if (skipBeforeFlag) {
				sourceStream.Position += skipBytes;
				sourceStreamOffset += skipBytes;
			}
			byte flags = (byte)sourceStream.ReadByte();
			if (flipFlagsBits)
				flags = (byte)~flags;
			if (!skipBeforeFlag) {
				sourceStream.Position += skipBytes;
				sourceStreamOffset += skipBytes;
			}

			byte allFlags = (byte)(desEncryptedFlag | deflatedFlag | bitwiseNotEncryptedFlag);
			if ((flags & ~allFlags) != 0)
				Logger.w("Found unknown resource encryption flags: 0x{0:X2}", flags);

			if ((flags & desEncryptedFlag) != 0) {
				var memStream = new MemoryStream((int)resourceStream.Length);
				using (var provider = new DESCryptoServiceProvider()) {
					var iv = new byte[8];
					sourceStream.Read(iv, 0, 8);
					provider.IV = iv;
					provider.Key = GetKey(sourceStream);

					using (var transform = provider.CreateDecryptor()) {
						while (true) {
							int count = sourceStream.Read(buffer1, 0, buffer1.Length);
							if (count <= 0)
								break;
							int count2 = transform.TransformBlock(buffer1, 0, count, buffer2, 0);
							memStream.Write(buffer2, 0, count2);
						}
						var finalData = transform.TransformFinalBlock(buffer1, 0, 0);
						memStream.Write(finalData, 0, finalData.Length);
					}
				}
				sourceStream = memStream;
				sourceStreamOffset = 0;
				didSomething = true;
			}

			if ((flags & deflatedFlag) != 0) {
				var memStream = new MemoryStream((int)resourceStream.Length);
				sourceStream.Position = sourceStreamOffset;
				using (var inflater = new DeflateStream(sourceStream, CompressionMode.Decompress)) {
					while (true) {
						int count = inflater.Read(buffer1, 0, buffer1.Length);
						if (count <= 0)
							break;
						memStream.Write(buffer1, 0, count);
					}
				}

				sourceStream = memStream;
				sourceStreamOffset = 0;
				didSomething = true;
			}

			if ((flags & bitwiseNotEncryptedFlag) != 0) {
				var memStream = new MemoryStream((int)resourceStream.Length);
				sourceStream.Position = sourceStreamOffset;
				for (int i = sourceStreamOffset; i < sourceStream.Length; i++)
					memStream.WriteByte((byte)~sourceStream.ReadByte());

				sourceStream = memStream;
				sourceStreamOffset = 0;
				didSomething = true;
			}

			if (didSomething && sourceStream is MemoryStream) {
				var memStream = (MemoryStream)sourceStream;
				return memStream.ToArray();
			}
			else {
				int len = (int)(sourceStream.Length - sourceStream.Position);
				byte[] data = new byte[len];
				sourceStream.Read(data, 0, len);
				return data;
			}
		}

		byte[] GetKey(Stream resourceStream) {
			byte[] key = new byte[8];
			resourceStream.Read(key, 0, key.Length);
			for (int i = 0; i < key.Length; i++) {
				if (key[i] != 0)
					return key;
			}
			key = PublicKeyBase.GetRawData(module.Assembly.PublicKeyToken);
			if (key == null)
				throw new ApplicationException("PublicKeyToken is null, can't decrypt resources");
			return key;
		}
	}
}
