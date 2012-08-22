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
using System.IO.Compression;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class ResourceDecrypter {
		const int BUFLEN = 0x8000;
		ModuleDefinition module;
		TypeDefinition resourceDecrypterType;
		byte[] buffer1 = new byte[BUFLEN];
		byte[] buffer2 = new byte[BUFLEN];
		byte desEncryptedFlag;
		byte deflatedFlag;
		byte bitwiseNotEncryptedFlag;
		FrameworkType frameworkType;
		bool flipFlagsBits;
		int skipBytes;

		public ResourceDecrypter(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			frameworkType = DotNetUtils.getFrameworkType(module);
			find(simpleDeobfuscator);
		}

		void find(ISimpleDeobfuscator simpleDeobfuscator) {
			switch (frameworkType) {
			case FrameworkType.Desktop:
				if (module.Runtime >= TargetRuntime.Net_2_0)
					findDesktopOrCompactFramework();
				else
					findDesktopOrCompactFrameworkV1();
				break;

			case FrameworkType.Silverlight:
				findSilverlight();
				break;

			case FrameworkType.CompactFramework:
				if (module.Runtime >= TargetRuntime.Net_2_0) {
					if (findDesktopOrCompactFramework())
						break;
				}
				findDesktopOrCompactFrameworkV1();
				break;
			}

			initializeHeaderInfo(simpleDeobfuscator);
		}

		static string[] requiredTypes = new string[] {
			"System.IO.MemoryStream",
			"System.Object",
			"System.Int32",
		};
		bool findDesktopOrCompactFramework() {
			resourceDecrypterType = null;
			foreach (var type in module.Types) {
				if (type.Fields.Count != 5)
					continue;
				if (!new FieldTypes(type).exactly(requiredTypes))
					continue;

				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;

				if (!checkCctor(cctor))
					continue;

				resourceDecrypterType = type;
				return true;
			}
			return false;
		}

		bool checkCctor(MethodDefinition cctor) {
			if (cctor.Body == null)
				return false;
			int stsfldCount = 0;
			foreach (var instr in cctor.Body.Instructions) {
				if (instr.OpCode.Code == Code.Stsfld) {
					var field = instr.Operand as FieldReference;
					if (!MemberReferenceHelper.compareTypes(cctor.DeclaringType, field.DeclaringType))
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
		bool findDesktopOrCompactFrameworkV1() {
			resourceDecrypterType = null;
			foreach (var type in module.Types) {
				if (type.Fields.Count != 0)
					continue;

				var method = getDecrypterMethod(type);
				if (method == null)
					continue;
				if (!new LocalTypes(method).exactly(requiredLocals_v1))
					continue;
				if (!DotNetUtils.callsMethod(method, "System.Int64", "()"))
					continue;
				if (!DotNetUtils.callsMethod(method, "System.Int32", "(System.Byte[],System.Int32,System.Int32)"))
					continue;
				if (!DotNetUtils.callsMethod(method, "System.Void", "(System.Array,System.Int32,System.Array,System.Int32,System.Int32)"))
					continue;
				if (!DotNetUtils.callsMethod(method, "System.Security.Cryptography.ICryptoTransform", "()"))
					continue;
				if (!DotNetUtils.callsMethod(method, "System.Byte[]", "(System.Byte[],System.Int32,System.Int32)"))
					continue;

				resourceDecrypterType = type;
				return true;
			}
			return false;
		}

		static string[] requiredLocals_sl = new string[] {
			"System.Byte",
			"System.Byte[]",
			"System.Int32",
		};
		void findSilverlight() {
			foreach (var type in module.Types) {
				if (type.Fields.Count > 0)
					continue;
				if (type.HasNestedTypes || type.HasGenericParameters)
					continue;
				var method = getDecrypterMethod(type);
				if (method == null)
					continue;
				if (!new LocalTypes(method).exactly(requiredLocals_sl))
					continue;

				resourceDecrypterType = type;
				break;
			}
		}

		void initializeHeaderInfo(ISimpleDeobfuscator simpleDeobfuscator) {
			skipBytes = 0;

			if (resourceDecrypterType != null) {
				if (updateFlags(getDecrypterMethod(), simpleDeobfuscator))
					return;
			}

			desEncryptedFlag = 1;
			deflatedFlag = 2;
			bitwiseNotEncryptedFlag = 4;
		}

		static bool checkFlipBits(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldloc = instrs[i];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				var local = DotNetUtils.getLocalVar(method.Body.Variables, ldloc);
				if (local == null || !local.VariableType.IsPrimitive)
					continue;

				var not = instrs[i + 1];
				if (not.OpCode.Code != Code.Not)
					continue;

				return true;
			}

			return false;
		}

		bool updateFlags(MethodDefinition method, ISimpleDeobfuscator simpleDeobfuscator) {
			if (method == null || method.Body == null)
				return false;

			var constants = new List<int>();
			simpleDeobfuscator.deobfuscate(method);
			var instructions = method.Body.Instructions;
			for (int i = 2; i < instructions.Count; i++) {
				var and = instructions[i];
				if (and.OpCode.Code != Code.And)
					continue;
				var ldci4 = instructions[i - 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				int flagValue = DotNetUtils.getLdcI4Value(ldci4);
				if (!isFlag(flagValue))
					continue;
				var ldloc = instructions[i - 2];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				var local = DotNetUtils.getLocalVar(method.Body.Variables, ldloc);
				if (!local.VariableType.IsPrimitive)
					continue;
				constants.Add(flagValue);
			}

			flipFlagsBits = checkFlipBits(method);
			skipBytes = getHeaderSkipBytes(method);

			switch (frameworkType) {
			case FrameworkType.Desktop:
				if (module.Runtime >= TargetRuntime.Net_2_0) {
					if (constants.Count == 2) {
						desEncryptedFlag = (byte)constants[0];
						deflatedFlag = (byte)constants[1];
						return true;
					}
				}
				else {
					if (constants.Count == 1) {
						desEncryptedFlag = (byte)constants[0];
						return true;
					}
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

		static int getHeaderSkipBytes(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (DotNetUtils.getLdcI4Value(ldci4) != 2)
					continue;
				var blt = instrs[i + 1];
				if (blt.OpCode.Code != Code.Blt && blt.OpCode.Code != Code.Blt_S)
					continue;
				return 1;
			}
			return 0;
		}

		static bool isFlag(int value) {
			for (uint tmp = (uint)value; tmp != 0; tmp >>= 1) {
				if ((tmp & 1) != 0)
					return tmp == 1;
			}
			return false;
		}

		MethodDefinition getDecrypterMethod() {
			return getDecrypterMethod(resourceDecrypterType);
		}

		static MethodDefinition getDecrypterMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.IO.Stream)"))
					return method;
				if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.Int32,System.IO.Stream)"))
					return method;
				if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.Int16,System.IO.Stream)"))
					return method;
				if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte,System.IO.Stream)"))
					return method;
				if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.SByte,System.IO.Stream)"))
					return method;
				if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte,System.IO.Stream,System.Int32)"))
					return method;
				if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.SByte,System.IO.Stream,System.UInt32)"))
					return method;
			}
			return null;
		}

		public byte[] decrypt(Stream resourceStream) {
			byte flags = (byte)resourceStream.ReadByte();
			if (flipFlagsBits)
				flags = (byte)~flags;
			Stream sourceStream = resourceStream;
			int sourceStreamOffset = 1;
			bool didSomething = false;

			sourceStream.Position += skipBytes;
			sourceStreamOffset += skipBytes;

			byte allFlags = (byte)(desEncryptedFlag | deflatedFlag | bitwiseNotEncryptedFlag);
			if ((flags & ~allFlags) != 0)
				Log.w("Found unknown resource encryption flags: 0x{0:X2}", flags);

			if ((flags & desEncryptedFlag) != 0) {
				var memStream = new MemoryStream((int)resourceStream.Length);
				using (var provider = new DESCryptoServiceProvider()) {
					var iv = new byte[8];
					sourceStream.Read(iv, 0, 8);
					provider.IV = iv;
					provider.Key = getKey(sourceStream);

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

		byte[] getKey(Stream resourceStream) {
			byte[] key = new byte[8];
			resourceStream.Read(key, 0, key.Length);
			for (int i = 0; i < key.Length; i++) {
				if (key[i] != 0)
					return key;
			}
			key = module.Assembly.Name.PublicKeyToken;
			if (key == null)
				throw new ApplicationException("PublicKeyToken is null, can't decrypt resources");
			return key;
		}
	}
}
