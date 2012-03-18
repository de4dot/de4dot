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
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class StringDecrypter {
		ModuleDefinition module;
		TypeDefinition stringType;
		MethodDefinition stringMethod;
		TypeDefinition dataDecrypterType;
		short s1, s2, s3;
		int i1, i2, i3, i4, i5, i6;
		bool checkMinus2;
		bool usePublicKeyToken;
		int keyLen;
		byte[] theKey;
		int magic1;
		uint rldFlag, bytesFlag;
		EmbeddedResource encryptedResource;
		BinaryReader reader;
		DecrypterType decrypterType;
		StreamHelperType streamHelperType;
		bool isV32OrLater;

		class StreamHelperType {
			public TypeDefinition type;
			public MethodDefinition readInt16Method;
			public MethodDefinition readInt32Method;
			public MethodDefinition readBytesMethod;

			public bool Detected {
				get {
					return readInt16Method != null &&
						  readInt32Method != null &&
						  readBytesMethod != null;
				}
			}

			public StreamHelperType(TypeDefinition type) {
				this.type = type;

				foreach (var method in type.Methods) {
					if (method.IsStatic || method.Body == null || method.IsPrivate || method.GenericParameters.Count > 0)
						continue;
					if (DotNetUtils.isMethod(method, "System.Int16", "()"))
						readInt16Method = method;
					else if (DotNetUtils.isMethod(method, "System.Int32", "()"))
						readInt32Method = method;
					else if (DotNetUtils.isMethod(method, "System.Byte[]", "(System.Int32)"))
						readBytesMethod = method;
				}
			}
		}

		public TypeDefinition Type {
			get { return stringType; }
		}

		public EmbeddedResource Resource {
			get { return encryptedResource; }
		}

		public IEnumerable<TypeDefinition> Types {
			get {
				return new List<TypeDefinition> {
					stringType,
					dataDecrypterType,
				};
			}
		}

		public MethodDefinition Method {
			get { return stringMethod; }
		}

		public bool Detected {
			get { return stringType != null; }
		}

		public StringDecrypter(ModuleDefinition module, DecrypterType decrypterType) {
			this.module = module;
			this.decrypterType = decrypterType;
		}

		static bool checkIfV32OrLater(TypeDefinition type) {
			int numInts = 0;
			foreach (var field in type.Fields) {
				if (field.FieldType.EType == ElementType.I4)
					numInts++;
			}
			return numInts >= 2;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (!checkType(type))
					continue;

				foreach (var method in type.Methods) {
					if (!checkDecrypterMethod(method))
						continue;

					stringType = type;
					stringMethod = method;
					isV32OrLater = checkIfV32OrLater(stringType);
					return;
				}
			}
		}

		static string[] requiredFieldTypes = new string[] {
			"System.Byte[]",
			"System.Int16",
		};
		bool checkType(TypeDefinition type) {
			if (!new FieldTypes(type).all(requiredFieldTypes))
				return false;
			if (type.NestedTypes.Count == 0) {
				return DotNetUtils.findFieldType(type, "System.IO.BinaryReader", true) != null &&
					DotNetUtils.findFieldType(type, "System.Collections.Generic.Dictionary`2<System.Int32,System.String>", true) != null;
			}
			else if (type.NestedTypes.Count == 3) {
				streamHelperType = findStreamHelperType(type);
				return streamHelperType != null;
			}
			else if (type.NestedTypes.Count == 1) {
				return type.NestedTypes[0].IsEnum;
			}
			else
				return false;
		}

		static string[] streamHelperTypeFields = new string[] {
			"System.IO.Stream",
			"System.Byte[]",
		};
		static StreamHelperType findStreamHelperType(TypeDefinition type) {
			foreach (var field in type.Fields) {
				var nested = field.FieldType as TypeDefinition;
				if (nested == null)
					continue;
				if (nested.DeclaringType != type)
					continue;
				if (!new FieldTypes(nested).exactly(streamHelperTypeFields))
					continue;
				var streamHelperType = new StreamHelperType(nested);
				if (!streamHelperType.Detected)
					continue;

				return streamHelperType;
			}
			return null;
		}

		static string[] requiredLocalTypes = new string[] {
			"System.Boolean",
			"System.Byte[]",
			"System.Char[]",
			"System.Int16",
			"System.Int32",
			"System.Reflection.Assembly",
			"System.String",
		};
		static bool checkDecrypterMethod(MethodDefinition method) {
			if (method == null || !method.IsStatic || method.Body == null)
				return false;
			if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
				return false;
			if (!new LocalTypes(method).all(requiredLocalTypes))
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode != OpCodes.Callvirt)
					continue;
				var calledMethod = instr.Operand as MethodReference;
				if (calledMethod != null && calledMethod.FullName == "System.IO.Stream System.Reflection.Assembly::GetManifestResourceStream(System.String)")
					return true;
			}
			return false;
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator) {
			if (stringType == null)
				return;

			if (!findConstants(simpleDeobfuscator)) {
				if (encryptedResource == null)
					Log.w("Could not find encrypted resource. Strings cannot be decrypted.");
				else
					Log.w("Can't decrypt strings. Possibly a new Eazfuscator.NET version.");
				return;
			}
		}

		bool findConstants(ISimpleDeobfuscator simpleDeobfuscator) {
			if (!findResource(stringMethod))
				return false;

			simpleDeobfuscator.deobfuscate(stringMethod);

			checkMinus2 = DeobUtils.hasInteger(stringMethod, -2);
			usePublicKeyToken = callsGetPublicKeyToken(stringMethod);

			var int64Method = findInt64Method(stringMethod);
			if (int64Method != null)
				decrypterType.Type = int64Method.DeclaringType;

			if (!findShorts(stringMethod))
				return false;
			if (!findInt3(stringMethod))
				return false;
			if (!findInt4(stringMethod))
				return false;
			if (checkMinus2 && !findInt5(stringMethod))
				return false;
			dataDecrypterType = findDataDecrypterType(stringMethod);
			if (dataDecrypterType == null)
				return false;

			if (isV32OrLater) {
				bool initializedAll;
				if (!findInts(stringMethod, out initializedAll))
					return false;

				var cctor = DotNetUtils.getMethod(stringType, ".cctor");
				if (!initializedAll && cctor != null) {
					simpleDeobfuscator.deobfuscate(cctor);
					if (!findIntsCctor(cctor))
						return false;
				}

				if (decrypterType.Detected && !decrypterType.initialize())
					return false;
			}

			initializeFlags();
			initialize();

			return true;
		}

		void initializeFlags() {
			if (!isV32OrLater || !DeobUtils.hasInteger(stringMethod, 0xFFFFFFF)) {
				// <= 3.3.134
				rldFlag = 0x40000000;
				bytesFlag = 0x80000000;
				return;
			}

			// 3.3.136+
			var instrs = stringMethod.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (DotNetUtils.getLdcI4Value(ldci4) != 0xFFFFFFF)
					continue;
				if (findFlags(stringMethod, i))
					return;
			}

			throw new ApplicationException("Could not find string decrypter flags");
		}

		bool findFlags(MethodDefinition method, int index) {
			var flags = new List<uint>(3);
			for (int i = index - 1; i >= 0; i--) {
				var instr = method.Body.Instructions[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
				if (!DotNetUtils.isLdcI4(instr))
					continue;
				uint value = (uint)DotNetUtils.getLdcI4Value(instr);
				if (value != 0x80000000 && value != 0x40000000 && value != 0x20000000)
					continue;
				flags.Add(value);
				if (flags.Count != 3)
					continue;

				rldFlag = flags[1];
				bytesFlag = flags[2];
				return true;
			}

			return false;
		}

		void initialize() {
			reader = new BinaryReader(encryptedResource.GetResourceStream());
			short len = (short)(reader.ReadInt16() ^ s1);
			if (len != 0)
				theKey = reader.ReadBytes(len);
			else
				keyLen = reader.ReadInt16() ^ s2;

			magic1 = i1 ^ i2;
			if (decrypterType.Detected)
				magic1 ^= (int)decrypterType.getMagic();
		}

		public string decrypt(int val) {
			while (true) {
				int offset = magic1 ^ i3 ^ val ^ i6;
				reader.BaseStream.Position = offset;
				byte[] tmpKey;
				if (theKey == null) {
					tmpKey = reader.ReadBytes(keyLen == -1 ? (short)(reader.ReadInt16() ^ s3 ^ offset) : keyLen);
					if (isV32OrLater) {
						for (int i = 0; i < tmpKey.Length; i++)
							tmpKey[i] ^= (byte)(magic1 >> ((i & 3) << 3));
					}
				}
				else
					tmpKey = theKey;

				int flags = i4 ^ magic1 ^ offset ^ reader.ReadInt32();
				if (checkMinus2 && flags == -2) {
					var ary2 = reader.ReadBytes(4);
					val = -(magic1 ^ i5) ^ (ary2[2] | (ary2[0] << 8) | (ary2[3] << 16) | (ary2[1] << 24));
					continue;
				}

				var bytes = reader.ReadBytes(flags & 0x1FFFFFFF);
				decrypt1(bytes, tmpKey);
				var pkt = module.Assembly.Name.PublicKeyToken;
				if (usePublicKeyToken && pkt != null && pkt.Length != 0) {
					for (int i = 0; i < bytes.Length; i++)
						bytes[i] ^= (byte)((pkt[i & 7] >> 5) + (pkt[i & 7] << 3));
				}

				if ((flags & rldFlag) != 0)
					bytes = rld(bytes);
				if ((flags & bytesFlag) != 0) {
					var sb = new StringBuilder(bytes.Length);
					foreach (var b in bytes)
						sb.Append((char)b);
					return sb.ToString();
				}
				else
					return Encoding.Unicode.GetString(bytes);
			}
		}

		static byte[] rld(byte[] src) {
			var dst = new byte[src[2] + (src[3] << 8) + (src[0] << 16) + (src[1] << 24)];
			int srcIndex = 4;
			int dstIndex = 0;
			int flags = 0;
			int bit = 128;
			while (dstIndex < dst.Length) {
				bit <<= 1;
				if (bit == 256) {
					bit = 1;
					flags = src[srcIndex++];
				}

				if ((flags & bit) == 0) {
					dst[dstIndex++] = src[srcIndex++];
					continue;
				}

				int numBytes = (src[srcIndex] >> 2) + 3;
				int copyIndex = dstIndex - ((src[srcIndex + 1] + (src[srcIndex] << 8)) & 0x3FF);
				if (copyIndex < 0)
					break;
				while (dstIndex < dst.Length && numBytes-- > 0)
					dst[dstIndex++] = dst[copyIndex++];
				srcIndex += 2;
			}

			return dst;
		}

		static void decrypt1(byte[] dest, byte[] key) {
			byte b = (byte)((key[1] + 7) ^ (dest.Length + 11));
			uint lcg = (uint)((key[0] | (key[2] << 8)) + (b << 3));
			b += 3;
			ushort xn = 0;
			for (int i = 0; i < dest.Length; i++) {
				if ((i & 1) == 0) {
					lcg = lcgNext(lcg);
					xn = (ushort)(lcg >> 16);
				}
				byte tmp = dest[i];
				dest[i] ^= (byte)(key[1] ^ xn ^ b);
				b = (byte)(tmp + 3);
				xn >>= 8;
			}
		}

		static uint lcgNext(uint lcg) {
			return lcg * 214013 + 2531011;
		}

		bool findResource(MethodDefinition method) {
			encryptedResource = DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
			return encryptedResource != null;
		}

		static MethodDefinition findInt64Method(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int64", "()"))
					continue;

				return calledMethod;
			}
			return null;
		}

		static TypeDefinition findDataDecrypterType(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Byte[]", "(System.Byte[],System.Byte[])"))
					continue;

				return calledMethod.DeclaringType;
			}
			return null;
		}

		bool findShorts(MethodDefinition method) {
			int index = 0;
			if (!findShort(method, ref index, ref s1))
				return false;
			if (!findShort(method, ref index, ref s2))
				return false;
			if (!findShort(method, ref index, ref s3))
				return false;

			return true;
		}

		bool findShort(MethodDefinition method, ref int index, ref short s) {
			if (!findCallReadInt16(method, ref index))
				return false;
			index++;
			return EfUtils.getInt16(method, ref index, ref s);
		}

		bool findInts(MethodDefinition method, out bool initializedAll) {
			initializedAll = false;

			// <= 3.2
			int index = findIndexInt64Method_v1(method);
			if (index >= 0) {
				if (!EfUtils.getNextInt32(method, ref index, out i1))
					return false;
				int tmp;
				if (!EfUtils.getNextInt32(method, ref index, out tmp))
					return false;
				if (!EfUtils.getNextInt32(method, ref index, out i2))
					return false;

				initializedAll = true;
				return true;
			}

			// 3.3
			index = findIndexInt64Method_v2(method);
			if (index >= 0) {
				if (!EfUtils.getNextInt32(method, ref index, out i2))
					return false;

				return true;
			}

			// 3.2+ (Silverlight)
			index = DeobUtils.indexOfLdci4Instruction(method, 268435314);
			if (index >= 0) {
				index--;
				index = EfUtils.indexOfPreviousLdci4Instruction(method, index);
				if (index < 0)
					return false;

				i1 = 0;
				if (!EfUtils.getNextInt32(method, ref index, out i2))
					return false;

				// 3.2: true, 3.3+: false
				initializedAll = method.Body.Instructions[index].OpCode.Code == Code.Stsfld;
				return true;
			}

			return false;
		}

		bool findIntsCctor(MethodDefinition cctor) {
			int index = 0;
			if (!findCallGetFrame(cctor, ref index))
				return findIntsCctor2(cctor);

			int tmp1, tmp2, tmp3;
			if (!EfUtils.getNextInt32(cctor, ref index, out tmp1))
				return false;
			if (!EfUtils.getNextInt32(cctor, ref index, out tmp2))
				return false;

			index = 0;
			while (true) {
				if (!EfUtils.getNextInt32(cctor, ref index, out tmp3))
					return false;
				int nextIndex = index;
				int tmp4;
				if (!EfUtils.getNextInt32(cctor, ref index, out tmp4))
					return false;
				if (tmp4 == 16)
					break;
				index = nextIndex;
			}

			i1 = tmp1 ^ tmp2 ^ tmp3;
			return true;
		}

		// Compact Framework doesn't have StackFrame
		bool findIntsCctor2(MethodDefinition cctor) {
			int index = 0;
			var instrs = cctor.Body.Instructions;
			while (index >= 0) {
				int val;
				if (!EfUtils.getNextInt32(cctor, ref index, out val))
					break;
				if (index < instrs.Count && instrs[index].OpCode.Code == Code.Add) {
					i1 = val;
					return true;
				}
			}

			return false;
		}

		bool findInt3(MethodDefinition method) {
			if (!isV32OrLater)
				return findInt3Old(method);
			return findInt3New(method);
		}

		// <= 3.1
		bool findInt3Old(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				var ldarg0 = instrs[i];
				if (ldarg0.OpCode.Code != Code.Ldarg_0)
					continue;

				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				int index = i + 1;
				int value;
				if (!EfUtils.getInt32(method, ref index, out value))
					continue;
				if (index >= instrs.Count)
					continue;

				if (instrs[index].OpCode.Code != Code.Xor)
					continue;

				i3 = value;
				return true;
			}

			return false;
		}

		// 3.2+
		bool findInt3New(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				var ldarg0 = instrs[i];
				if (ldarg0.OpCode.Code != Code.Ldarg_0)
					continue;

				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;

				if (!DotNetUtils.isLdloc(instrs[i + 3]))
					continue;

				if (instrs[i + 4].OpCode.Code != Code.Xor)
					continue;

				i3 = DotNetUtils.getLdcI4Value(ldci4);
				if (!findInt6(method, i + 5))
					return false;
				return true;
			}

			return false;
		}

		// v3.3.134.30672+ (not 3.3.128.10407)
		bool findInt6(MethodDefinition method, int index) {
			index = getNextLdci4InSameBlock(method, index);
			if (index < 0)
				return true;

			return EfUtils.getNextInt32(method, ref index, out i6);
		}

		bool findInt4(MethodDefinition method) {
			int index = 0;
			if (!findCallReadInt32(method, ref index))
				return false;
			if (!EfUtils.getNextInt32(method, ref index, out i4))
				return false;

			return true;
		}

		static int getNextLdci4InSameBlock(MethodDefinition method, int index) {
			var instrs = method.Body.Instructions;
			for (int i = index; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					return -1;
				if (DotNetUtils.isLdcI4(instr))
					return i;
			}

			return -1;
		}

		bool findInt5(MethodDefinition method) {
			int index = -1;
			while (true) {
				index++;
				if (!findCallReadBytes(method, ref index))
					return false;
				if (index <= 0)
					continue;
				var ldci4 = method.Body.Instructions[index - 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (DotNetUtils.getLdcI4Value(ldci4) != 4)
					continue;
				if (!EfUtils.getNextInt32(method, ref index, out i5))
					return false;

				return true;
			}
		}

		static int findIndexInt64Method_v1(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				var stloc = instrs[i + 1];
				if (!DotNetUtils.isStloc(stloc))
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int64", "()"))
					continue;

				return i;
			}

			return -1;
		}

		static int findIndexInt64Method_v2(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var call = instrs[i];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int64", "()"))
					continue;

				return i;
			}
			return -1;
		}

		static bool callsGetPublicKeyToken(MethodDefinition method) {
			int index = 0;
			return findCall(method, ref index, "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()");
		}

		bool findCallReadInt16(MethodDefinition method, ref int index) {
			return findCall(method, ref index, streamHelperType == null ? "System.Int16 System.IO.BinaryReader::ReadInt16()" : streamHelperType.readInt16Method.FullName);
		}

		bool findCallReadInt32(MethodDefinition method, ref int index) {
			return findCall(method, ref index, streamHelperType == null ? "System.Int32 System.IO.BinaryReader::ReadInt32()" : streamHelperType.readInt32Method.FullName);
		}

		bool findCallReadBytes(MethodDefinition method, ref int index) {
			return findCall(method, ref index, streamHelperType == null ? "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)" : streamHelperType.readBytesMethod.FullName);
		}

		static bool findCallGetFrame(MethodDefinition method, ref int index) {
			return findCall(method, ref index, "System.Diagnostics.StackFrame System.Diagnostics.StackTrace::GetFrame(System.Int32)");
		}

		static bool findCall(MethodDefinition method, ref int index, string methodFullName) {
			for (; index < method.Body.Instructions.Count; index++) {
				if (!findCallvirt(method, ref index))
					return false;

				var calledMethod = method.Body.Instructions[index].Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (calledMethod.ToString() != methodFullName)
					continue;

				return true;
			}
			return false;
		}

		static bool findCallvirt(MethodDefinition method, ref int index) {
			var instrs = method.Body.Instructions;
			for (; index < instrs.Count; index++) {
				var instr = instrs[index];
				if (instr.OpCode.Code != Code.Callvirt)
					continue;

				return true;
			}

			return false;
		}
	}
}
