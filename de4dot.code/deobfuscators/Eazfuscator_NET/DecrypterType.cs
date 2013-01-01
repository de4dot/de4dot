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
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class DecrypterType {
		ModuleDefMD module;
		ISimpleDeobfuscator simpleDeobfuscator;
		TypeDef type;
		MethodDef int64Method;
		bool initialized;
		ulong l1;
		int i1, i2, i3;
		int m1_i1, m2_i1, m2_i2, m3_i1;
		MethodDef[] efConstMethods;

		public MethodDef Int64Method {
			get { return int64Method; }
		}

		public TypeDef Type {
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

		public DecrypterType(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public bool initialize() {
			if (initialized)
				return true;

			int64Method = findInt64Method();
			if (int64Method == null)
				return false;

			if (!initializeEfConstMethods())
				return false;
			if (!findInt1And2())
				return false;
			if (!findInt3())
				return false;
			if (!findMethodInts())
				return false;

			initialized = true;
			return true;
		}

		bool initializeEfConstMethods() {
			if (type == null)
				return false;

			efConstMethods = new MethodDef[6];

			efConstMethods[0] = findEfConstMethodCall(int64Method);
			efConstMethods[5] = findEfConstMethodCall(efConstMethods[0]);
			efConstMethods[4] = findEfConstMethodCall(efConstMethods[5]);
			var calls = findEfConstMethodCalls(efConstMethods[4]);
			if (calls.Count != 2)
				return false;
			if (getNumberOfTypeofs(calls[0]) == 3) {
				efConstMethods[2] = calls[0];
				efConstMethods[1] = calls[1];
			}
			else {
				efConstMethods[2] = calls[0];
				efConstMethods[1] = calls[1];
			}
			efConstMethods[3] = findEfConstMethodCall(efConstMethods[1]);

			foreach (var m in efConstMethods) {
				if (m == null)
					return false;
			}
			return true;
		}

		static int getNumberOfTypeofs(MethodDef method) {
			if (method == null)
				return 0;
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldtoken)
					count++;
			}
			return count;
		}

		MethodDef findEfConstMethodCall(MethodDef method) {
			var list = findEfConstMethodCalls(method);
			if (list == null || list.Count != 1)
				return null;
			return list[0];
		}

		List<MethodDef> findEfConstMethodCalls(MethodDef method) {
			if (method == null)
				return null;
			var list = new List<MethodDef>();
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.Body == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int32", "()"))
					continue;
				if (type.NestedTypes.IndexOf(calledMethod.DeclaringType) < 0)
					continue;

				list.Add(calledMethod);
			}
			return list;
		}

		MethodDef findInt64Method() {
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

		bool findInt64(MethodDef method) {
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

		bool findInt1And2() {
			var consts = getConstants(efConstMethods[2]);
			if (consts.Count != 2)
				return false;
			i1 = consts[0];
			i2 = consts[1];
			return true;
		}

		bool findInt3() {
			var consts = getConstants(efConstMethods[5]);
			if (consts.Count != 1)
				return false;
			i3 = consts[0];
			return true;
		}

		bool findMethodInts() {
			foreach (var nestedType in type.NestedTypes) {
				var methods = getBinaryIntMethods(nestedType);
				if (methods.Count < 3)
					continue;
				foreach (var m in methods)
					simpleDeobfuscator.deobfuscate(m);
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

		static List<MethodDef> getBinaryIntMethods(TypeDef type) {
			var list = new List<MethodDef>();
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Int32", "(System.Int32,System.Int32)"))
					continue;

				list.Add(method);
			}
			return list;
		}

		bool findMethod1Int(IEnumerable<MethodDef> methods) {
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

		bool findMethod2Int(IEnumerable<MethodDef> methods) {
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

		bool findMethod3Int(IEnumerable<MethodDef> methods) {
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

		static int countInstructions(MethodDef method, Code code) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == code)
					count++;
			}
			return count;
		}

		static List<int> getConstants(MethodDef method) {
			var list = new List<int>();

			if (method == null)
				return list;

			int index = 0;
			var instrs = method.Body.Instructions;
			var constantsReader = new EfConstantsReader(method);
			while (true) {
				int val;
				if (!constantsReader.getNextInt32(ref index, out val))
					break;

				if (index < instrs.Count && instrs[index].OpCode.Code != Code.Ret)
					list.Add(val);
			}

			return list;
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
			return binOp3(binOp2(efConstMethods[1].DeclaringType.MDToken.ToInt32(), binOp3(efConstMethods[0].DeclaringType.MDToken.ToInt32(), efConstMethods[4].DeclaringType.MDToken.ToInt32())), constMethod6());
		}

		int constMethod2() {
			return binOp1(efConstMethods[2].DeclaringType.MDToken.ToInt32(), efConstMethods[3].DeclaringType.MDToken.ToInt32() ^ binOp2(efConstMethods[1].DeclaringType.MDToken.ToInt32(), binOp3(efConstMethods[5].DeclaringType.MDToken.ToInt32(), constMethod4())));
		}

		int constMethod3() {
			return binOp3(binOp1(constMethod2() ^ i1, efConstMethods[3].DeclaringType.MDToken.ToInt32()), binOp2(efConstMethods[0].DeclaringType.MDToken.ToInt32() ^ efConstMethods[5].DeclaringType.MDToken.ToInt32(), i2));
		}

		int constMethod4() {
			return binOp3(efConstMethods[3].DeclaringType.MDToken.ToInt32(), binOp1(efConstMethods[0].DeclaringType.MDToken.ToInt32(), binOp2(efConstMethods[1].DeclaringType.MDToken.ToInt32(), binOp3(efConstMethods[2].DeclaringType.MDToken.ToInt32(), binOp1(efConstMethods[4].DeclaringType.MDToken.ToInt32(), efConstMethods[5].DeclaringType.MDToken.ToInt32())))));
		}

		int constMethod5() {
			return binOp2(binOp2(constMethod3(), binOp1(efConstMethods[4].DeclaringType.MDToken.ToInt32(), constMethod2())), efConstMethods[5].DeclaringType.MDToken.ToInt32());
		}

		int constMethod6() {
			return binOp1(efConstMethods[5].DeclaringType.MDToken.ToInt32(), binOp3(binOp2(efConstMethods[4].DeclaringType.MDToken.ToInt32(), efConstMethods[0].DeclaringType.MDToken.ToInt32()), binOp3(efConstMethods[2].DeclaringType.MDToken.ToInt32() ^ i3, constMethod5())));
		}

		public ulong getMagic() {
			if (type == null)
				throw new ApplicationException("Can't calculate magic since type isn't initialized");

			var bytes = new List<byte>();
			if (module.Assembly != null) {
				if (!PublicKeyBase.IsNullOrEmpty2(module.Assembly.PublicKey))
					bytes.AddRange(module.Assembly.PublicKeyToken.Data);
				bytes.AddRange(Encoding.Unicode.GetBytes(module.Assembly.Name.String));
			}
			int cm1 = constMethod1();
			bytes.Add((byte)(type.MDToken.ToInt32() >> 24));
			bytes.Add((byte)(cm1 >> 16));
			bytes.Add((byte)(type.MDToken.ToInt32() >> 8));
			bytes.Add((byte)cm1);
			bytes.Add((byte)(type.MDToken.ToInt32() >> 16));
			bytes.Add((byte)(cm1 >> 8));
			bytes.Add((byte)type.MDToken.ToInt32());
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
