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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class DecrypterType {
		TypeDefinition type;
		MethodDefinition int64Method;
		bool initialized;
		ulong l1;
		int i1, i2;
		int m1_i1, m2_i1, m2_i2, m3_i1;
		int token1, token2, token3, token4, token5, token6;

		public TypeDefinition Type {
			get { return type; }
			set {
				if (type == null)
					type = value;
				else if (type != value)
					throw new ApplicationException("Found another one");
			}
		}

		public bool Detected {
			get { return type != null; }
		}

		public bool initialize() {
			if (initialized)
				return true;

			int64Method = findInt64Method();
			if (int64Method == null)
				return false;

			if (!findInt1())
				return false;
			if (!findInt2())
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

			initialized = true;
			return true;
		}

		MethodDefinition findInt64Method() {
			if (type == null)
				return null;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.HasGenericParameters)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Int64", "()"))
					continue;
				if (!findInt64(method))
					continue;

				return method;
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

		bool findInt1() {
			var consts = getConstants(findNestedTypeMethod(1294352278));
			if (consts.Count != 2)
				return false;
			i1 = consts[1];
			return true;
		}

		bool findInt2() {
			var consts = getConstants(findNestedTypeMethod(1106695601));
			if (consts.Count != 1)
				return false;
			i2 = consts[0];
			return true;
		}

		bool findMethodInts() {
			foreach (var nestedType in type.NestedTypes) {
				var methods = getBinaryIntMethods(nestedType);
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
				if (!EfUtils.getNextInt32(method, ref index, out val))
					break;

				if (index + 1 < instrs.Count && instrs[index].OpCode.Code != Code.Ret)
					list.Add(val);
			}

			return list;
		}

		MethodDefinition findNestedTypeMethod(int constant) {
			foreach (var nestedType in type.NestedTypes) {
				foreach (var method in nestedType.Methods) {
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

		int getToken(int constant) {
			var method = findNestedTypeMethod(constant);
			if (method == null)
				return 0;
			return method.DeclaringType.MetadataToken.ToInt32();
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
			return binOp3(binOp1(constMethod2() ^ 0x1F74F46E, token4), binOp2(token1 ^ token6, i1));
		}

		int constMethod4() {
			return binOp3(token4, binOp1(token1, binOp2(token2, binOp3(token3, binOp1(token5, token6)))));
		}

		int constMethod5() {
			return binOp2(binOp2(constMethod3(), binOp1(token5, constMethod2())), token6);
		}

		int constMethod6() {
			return binOp1(token6, binOp3(binOp2(token5, token1), binOp3(token3 ^ i2, constMethod5())));
		}

		public ulong getMagic() {
			if (type == null)
				throw new ApplicationException("Can't calculate magic since type isn't initialized");
			var module = type.Module;

			var bytes = new List<byte>();
			if (module.Assembly != null) {
				if (module.Assembly.Name.PublicKeyToken != null)
					bytes.AddRange(module.Assembly.Name.PublicKeyToken);
				bytes.AddRange(Encoding.Unicode.GetBytes(module.Assembly.Name.Name));
			}
			int cm1 = constMethod1();
			bytes.Add((byte)(type.MetadataToken.ToInt32() >> 24));
			bytes.Add((byte)(cm1 >> 16));
			bytes.Add((byte)(type.MetadataToken.ToInt32() >> 8));
			bytes.Add((byte)cm1);
			bytes.Add((byte)(type.MetadataToken.ToInt32() >> 16));
			bytes.Add((byte)(cm1 >> 8));
			bytes.Add((byte)type.MetadataToken.ToInt32());
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
			return magic ^ l1;
		}
	}
}
