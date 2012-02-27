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

using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class StringDecrypter {
		ModuleDefinition module;
		TypeDefinition decryptType;
		MethodDefinition decryptMethod;
		TypeDefinition dataDecrypterType;
		MethodDefinition int64Method;
		short s1, s2, s3;
		int i1, i2, i3, i4, i5, i6, i7;
		ulong l1;
		int m1_i1, m2_i1, m2_i2, m3_i1;
		int token1, token2, token3, token4, token5, token6;
		bool checkMinus2;
		bool usePublicKeyToken;
		int keyLen;
		byte[] theKey;
		int magic1;
		EmbeddedResource encryptedResource;
		BinaryReader reader;

		public TypeDefinition Type {
			get { return decryptType; }
		}

		public EmbeddedResource Resource {
			get { return encryptedResource; }
		}

		public IEnumerable<TypeDefinition> Types {
			get {
				var list = new List<TypeDefinition>();
				list.Add(decryptType);
				list.Add(dataDecrypterType);
				if (int64Method != null)
					list.Add(int64Method.DeclaringType);
				return list;
			}
		}

		public MethodDefinition Method {
			get { return decryptMethod; }
		}

		public bool Detected {
			get { return decryptType != null; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			foreach (var type in module.Types) {
				if (DotNetUtils.findFieldType(type, "System.IO.BinaryReader", true) == null)
					continue;
				if (DotNetUtils.findFieldType(type, "System.Collections.Generic.Dictionary`2<System.Int32,System.String>", true) == null)
					continue;

				foreach (var method in type.Methods) {
					if (!checkDecrypterMethod(method))
						continue;

					decryptType = type;
					decryptMethod = method;
					if (!findConstants(simpleDeobfuscator)) {
						if (encryptedResource == null)
							Log.w("Could not find encrypted resource. Strings cannot be decrypted.");
						else
							Log.w("Can't decrypt strings. Possibly a new Eazfuscator.NET version.");
					}
					return;
				}
			}
		}

		static bool checkDecrypterMethod(MethodDefinition method) {
			if (method == null || !method.IsStatic || method.Body == null)
				return false;
			if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
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

		public bool findConstants(ISimpleDeobfuscator simpleDeobfuscator) {
			if (!findResource(decryptMethod))
				return false;

			simpleDeobfuscator.deobfuscate(decryptMethod);

			checkMinus2 = DeobUtils.hasInteger(decryptMethod, -2);
			usePublicKeyToken = callsGetPublicKeyToken(decryptMethod);

			int64Method = findInt64Method(decryptMethod);

			if (!findShorts(decryptMethod))
				return false;
			if (!findInt3(decryptMethod))
				return false;
			if (!findInt4(decryptMethod))
				return false;
			if (checkMinus2 && !findInt7(decryptMethod))
				return false;
			dataDecrypterType = findDataDecrypterType(decryptMethod);
			if (dataDecrypterType == null)
				return false;

			if (int64Method != null) {
				if (!findInts(decryptMethod))
					return false;
				if (!findInt64(int64Method))
					return false;
				if (!findInt5())
					return false;
				if (!findInt6())
					return false;
				if (!findMethodInts())
					return false;
				token1 = getToken(-1509110933);
				token2 = getToken(-82806859);
				token3 = getToken(1294352278);
				token4 = getToken(402344241);
				token5 = getToken(-56237163);
				token6 = getToken(1106695601);
				if (token1 == 0 || token2 == 0 || token3 == 0)
					return false;
				if (token4 == 0 || token5 == 0 || token6 == 0)
					return false;
			}

			initialize();

			return true;
		}

		void initialize() {
			reader = new BinaryReader(encryptedResource.GetResourceStream());
			short len = (short)(reader.ReadInt16() ^ s1);
			if (len != 0)
				theKey = reader.ReadBytes(len);
			else
				keyLen = reader.ReadInt16() ^ s2;

			if (int64Method != null)
				magic1 = (int)getMagic() ^ i1 ^ i2;
		}

		public string decrypt(int val) {
			while (true) {
				int offset = magic1 ^ i3 ^ val;
				reader.BaseStream.Position = offset;
				byte[] tmpKey;
				if (theKey == null) {
					tmpKey = reader.ReadBytes(keyLen == -1 ? (short)(reader.ReadInt16() ^ s3 ^ offset) : keyLen);
					if (int64Method != null) {
						for (int i = 0; i < tmpKey.Length; i++)
							tmpKey[i] ^= (byte)(magic1 >> ((i & 3) << 3));
					}
				}
				else
					tmpKey = theKey;

				int flags = i4 ^ magic1 ^ offset ^ reader.ReadInt32();
				if (checkMinus2 && flags == -2) {
					var ary2 = reader.ReadBytes(4);
					val = -(magic1 ^ i7) ^ (ary2[2] | (ary2[0] << 8) | (ary2[3] << 16) | (ary2[1] << 24));
					continue;
				}

				var bytes = reader.ReadBytes(flags & 0x1FFFFFFF);
				decrypt1(bytes, tmpKey);
				var pkt = module.Assembly.Name.PublicKeyToken;
				if (usePublicKeyToken && pkt != null && pkt.Length != 0) {
					for (int i = 0; i < bytes.Length; i++)
						bytes[i] ^= (byte)((pkt[i & 7] >> 5) + (pkt[i & 7] << 3));
				}

				if ((flags & 0x40000000) != 0)
					bytes = rld(bytes);
				if ((flags & 0x80000000) != 0) {
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
			uint lcg = (uint)((key[0] | key[2] << 8) + (b << 3));
			b += 3;
			ushort xn = 0;
			for (int i = 0; i < dest.Length; i++) {
				if ((i & 1) == 0) {
					lcg = lcgNext(lcg);
					xn = (ushort)(lcg >> 16);
				}
				byte tmp = dest[i];
				dest[i] ^= (byte)(key[1] ^ (byte)xn ^ b);
				b = (byte)(tmp + 3);
				xn >>= 8;
			}
		}

		static uint lcgNext(uint lcg) {
			return lcg * 214013 + 2531011;
		}

		uint getMagic() {
			var bytes = new List<byte>();
			if (module.Assembly != null) {
				if (module.Assembly.Name.PublicKeyToken != null)
					bytes.AddRange(module.Assembly.Name.PublicKeyToken);
				bytes.AddRange(Encoding.Unicode.GetBytes(module.Assembly.Name.Name));
			}
			int cm1 = constMethod1();
			int token = int64Method.DeclaringType.MetadataToken.ToInt32();
			bytes.Add((byte)(token >> 24));
			bytes.Add((byte)(cm1 >> 16));
			bytes.Add((byte)(token >> 8));
			bytes.Add((byte)cm1);
			bytes.Add((byte)(token >> 16));
			bytes.Add((byte)(cm1 >> 8));
			bytes.Add((byte)token);
			bytes.Add((byte)(cm1 >> 24));

			ulong magic = 0;
			foreach (var b in bytes) {
				magic += b;
				magic += magic << 20;
				magic ^= magic >> 12;
			}
			magic += magic << 6;
			magic ^= magic >> 22;
			magic += magic << 30;
			return (uint)magic ^ (uint)l1;
		}

		bool findResource(MethodDefinition method) {
			encryptedResource = DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
			return encryptedResource != null;
		}

		int getToken(int constant) {
			var method = findNestedTypeMethod(constant);
			if (method == null)
				return 0;
			return method.DeclaringType.MetadataToken.ToInt32();
		}

		bool findInt5() {
			var consts = getConstants(findNestedTypeMethod(1294352278));
			if (consts.Count != 2)
				return false;
			i5 = consts[1];
			return true;
		}

		bool findInt6() {
			var consts = getConstants(findNestedTypeMethod(1106695601));
			if (consts.Count != 1)
				return false;
			i6 = consts[0];
			return true;
		}

		bool findMethodInts() {
			foreach (var type in int64Method.DeclaringType.NestedTypes) {
				var methods = getBinaryIntMethods(type);
				if (methods.Count < 3)
					continue;
				if (!findMethod1Int(methods))
					continue;
				if (!findMethod2Int(methods))
					continue;
				if (!findMethod3Int(methods))
					continue;

				return true;
			}
			return false;
		}

		static List<MethodDefinition> getBinaryIntMethods(TypeDefinition type) {
			var list = new List<MethodDefinition>();
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Int32", "(System.Int32,System.Int32)"))
					continue;

				list.Add(method);
			}
			return list;
		}

		bool findMethod1Int(IEnumerable<MethodDefinition> methods) {
			foreach (var method in methods) {
				if (countInstructions(method, Code.Ldarg_0) != 1)
					continue;
				var constants = getConstants(method);
				if (constants.Count != 1)
					continue;

				m1_i1 = constants[0];
				return true;
			}
			return false;
		}

		bool findMethod2Int(IEnumerable<MethodDefinition> methods) {
			foreach (var method in methods) {
				var constants = getConstants(method);
				if (constants.Count != 2)
					continue;

				m2_i1 = constants[0];
				m2_i2 = constants[1];
				return true;
			}
			return false;
		}

		bool findMethod3Int(IEnumerable<MethodDefinition> methods) {
			foreach (var method in methods) {
				if (countInstructions(method, Code.Ldarg_0) != 2)
					continue;
				var constants = getConstants(method);
				if (constants.Count != 1)
					continue;

				m3_i1 = constants[0];
				return true;
			}
			return false;
		}

		static int countInstructions(MethodDefinition method, Code code) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == code)
					count++;
			}
			return count;
		}

		static List<int> getConstants(MethodDefinition method) {
			var list = new List<int>();

			if (method == null)
				return list;

			int index = 0;
			var instrs = method.Body.Instructions;
			while (true) {
				int val;
				if (!getNextInt32(method, ref index, out val))
					break;

				if (index + 1 < instrs.Count && instrs[index].OpCode.Code != Code.Ret)
					list.Add(val);
			}

			return list;
		}

		MethodDefinition findNestedTypeMethod(int constant) {
			foreach (var type in int64Method.DeclaringType.NestedTypes) {
				foreach (var method in type.Methods) {
					if (!method.IsStatic || method.Body == null)
						continue;

					var instrs = method.Body.Instructions;
					for (int i = 0; i < instrs.Count - 1; i++) {
						var ldci4 = instrs[i];
						if (!DotNetUtils.isLdcI4(ldci4))
							continue;
						if (DotNetUtils.getLdcI4Value(ldci4) != constant)
							continue;
						if (instrs[i + 1].OpCode.Code != Code.Ret)
							continue;

						return method;
					}
				}
			}
			return null;
		}

		bool findInt64(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci8 = instrs[i];
				if (ldci8.OpCode.Code != Code.Ldc_I8)
					continue;

				if (instrs[i + 1].OpCode.Code != Code.Xor)
					continue;

				l1 = (ulong)(long)ldci8.Operand;
				return true;
			}
			return false;
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
			return getInt16(method, ref index, ref s);
		}

		bool findInts(MethodDefinition method) {
			int index = findIndexFirstIntegerConstant(method);
			if (index < 0)
				return false;

			if (!getNextInt32(method, ref index, out i1))
				return false;
			int tmp;
			if (!getNextInt32(method, ref index, out tmp))
				return false;
			if (!getNextInt32(method, ref index, out i2))
				return false;

			return true;
		}

		bool findInt3(MethodDefinition method) {
			if (int64Method == null)
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
				if (!getInt32(method, ref index, out value))
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
				return true;
			}

			return false;
		}

		bool findInt4(MethodDefinition method) {
			int index = 0;
			if (!findCallReadInt32(method, ref index))
				return false;
			if (!getNextInt32(method, ref index, out i4))
				return false;

			return true;
		}

		bool findInt7(MethodDefinition method) {
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
				if (!getNextInt32(method, ref index, out i7))
					return false;

				return true;
			}
		}

		static int findIndexFirstIntegerConstant(MethodDefinition method) {
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

		static bool getNextInt32(MethodDefinition method, ref int index, out int val) {
			for (; index < method.Body.Instructions.Count; index++) {
				var instr = method.Body.Instructions[index];
				if (instr.OpCode.Code != Code.Ldc_I4_S && instr.OpCode.Code != Code.Ldc_I4)
					continue;

				return getInt32(method, ref index, out val);
			}

			val = 0;
			return false;
		}

		static bool getInt16(MethodDefinition method, ref int index, ref short s) {
			int val;
			if (!getInt32(method, ref index, out val))
				return false;
			s = (short)val;
			return true;
		}

		static bool getInt32(MethodDefinition method, ref int index, out int val) {
			val = 0;
			var instrs = method.Body.Instructions;
			if (index >= instrs.Count)
				return false;
			var ldci4 = instrs[index];
			if (ldci4.OpCode.Code != Code.Ldc_I4_S && ldci4.OpCode.Code != Code.Ldc_I4)
				return false;

			var stack = new Stack<int>();
			stack.Push(DotNetUtils.getLdcI4Value(ldci4));

			index++;
			for (; index < instrs.Count; index++) {
				int l = stack.Count - 1;

				var instr = instrs[index];
				switch (instr.OpCode.Code) {
				case Code.Not:
					stack.Push(~stack.Pop());
					break;

				case Code.Neg:
					stack.Push(-stack.Pop());
					break;

				case Code.Ldc_I4:
				case Code.Ldc_I4_S:
				case Code.Ldc_I4_0:
				case Code.Ldc_I4_1:
				case Code.Ldc_I4_2:
				case Code.Ldc_I4_3:
				case Code.Ldc_I4_4:
				case Code.Ldc_I4_5:
				case Code.Ldc_I4_6:
				case Code.Ldc_I4_7:
				case Code.Ldc_I4_8:
				case Code.Ldc_I4_M1:
					stack.Push(DotNetUtils.getLdcI4Value(instr));
					break;

				case Code.Xor:
					if (stack.Count < 2)
						goto done;
					stack.Push(stack.Pop() ^ stack.Pop());
					break;

				default:
					goto done;
				}
			}
done:
			while (stack.Count > 1)
				stack.Pop();
			val = stack.Pop();
			return true;
		}

		static bool callsGetPublicKeyToken(MethodDefinition method) {
			int index = 0;
			return findCall(method, ref index, "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()");
		}

		static bool findCallReadInt16(MethodDefinition method, ref int index) {
			return findCall(method, ref index, "System.Int16 System.IO.BinaryReader::ReadInt16()");
		}

		static bool findCallReadInt32(MethodDefinition method, ref int index) {
			return findCall(method, ref index, "System.Int32 System.IO.BinaryReader::ReadInt32()");
		}

		static bool findCallReadBytes(MethodDefinition method, ref int index) {
			return findCall(method, ref index, "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)");
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

		int binOp1(int a, int b) {
			return a ^ (b - m1_i1);
		}

		int binOp2(int a, int b) {
			return (a - m2_i1) ^ (b + m2_i2);
		}

		int binOp3(int a, int b) {
			return a ^ (b - m3_i1) ^ (a - b);
		}

		int constMethod1() {
			return binOp3(binOp2(token2, binOp3(token1, token5)), constMethod6());
		}

		int constMethod2() {
			return binOp1(token3, token4 ^ binOp2(token2, binOp3(token6, constMethod4())));
		}

		int constMethod3() {
			return binOp3(binOp1(constMethod2() ^ 0x1F74F46E, token4), binOp2(token1 ^ token6, i5));
		}

		int constMethod4() {
			return binOp3(token4, binOp1(token1, binOp2(token2, binOp3(token3, binOp1(token5, token6)))));
		}

		int constMethod5() {
			return binOp2(binOp2(constMethod3(), binOp1(token5, constMethod2())), token6);
		}

		int constMethod6() {
			return binOp1(token6, binOp3(binOp2(token5, token1), binOp3(token3 ^ i6, constMethod5())));
		}
	}
}
