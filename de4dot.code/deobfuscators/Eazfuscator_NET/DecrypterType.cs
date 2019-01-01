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
		List<int> shiftConsts;

		public MethodDef Int64Method => int64Method;

		public TypeDef Type {
			get => type;
			set {
				if (type == null)
					type = value;
				else if (type != value)
					throw new ApplicationException("Found another one");
			}
		}

		public bool Detected => type != null;

		public List<int> ShiftConsts {
			get => shiftConsts;
			set {
				if (shiftConsts == null)
					shiftConsts = value;
				else if (shiftConsts != value)
					throw new ApplicationException("Found another one");
			}
		}

		public DecrypterType(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public bool Initialize() {
			if (initialized)
				return true;

			int64Method = FindInt64Method();
			if (int64Method == null)
				return false;

			if (!InitializeEfConstMethods())
				return false;
			if (!FindInt1And2())
				return false;
			if (!FindInt3())
				return false;
			if (!FindMethodInts())
				return false;

			initialized = true;
			return true;
		}

		bool InitializeEfConstMethods() {
			if (type == null)
				return false;

			efConstMethods = new MethodDef[6];

			efConstMethods[0] = FindEfConstMethodCall(int64Method);
			efConstMethods[5] = FindEfConstMethodCall(efConstMethods[0]);
			efConstMethods[4] = FindEfConstMethodCall(efConstMethods[5]);
			var calls = FindEfConstMethodCalls(efConstMethods[4]);
			if (calls.Count != 2)
				return false;
			if (GetNumberOfTypeofs(calls[0]) == 3) {
				efConstMethods[2] = calls[0];
				efConstMethods[1] = calls[1];
			}
			else {
				efConstMethods[2] = calls[0];
				efConstMethods[1] = calls[1];
			}
			efConstMethods[3] = FindEfConstMethodCall(efConstMethods[1]);

			foreach (var m in efConstMethods) {
				if (m == null)
					return false;
			}
			return true;
		}

		static int GetNumberOfTypeofs(MethodDef method) {
			if (method == null)
				return 0;
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldtoken)
					count++;
			}
			return count;
		}

		MethodDef FindEfConstMethodCall(MethodDef method) {
			var list = FindEfConstMethodCalls(method);
			if (list == null || list.Count != 1)
				return null;
			return list[0];
		}

		List<MethodDef> FindEfConstMethodCalls(MethodDef method) {
			if (method == null)
				return null;
			var list = new List<MethodDef>();
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Int32", "()"))
					continue;
				if (type.NestedTypes.IndexOf(calledMethod.DeclaringType) < 0)
					continue;

				list.Add(calledMethod);
			}
			return list;
		}

		MethodDef FindInt64Method() {
			if (type == null)
				return null;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.HasGenericParameters)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Int64", "()"))
					continue;
				if (!FindInt64(method))
					continue;

				return method;
			}

			return null;
		}

		bool FindInt64(MethodDef method) {
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

		bool FindInt1And2() {
			var consts = GetConstants(efConstMethods[2]);
			if (consts.Count != 2)
				return false;
			i1 = consts[0];
			i2 = consts[1];
			return true;
		}

		bool FindInt3() {
			var consts = GetConstants(efConstMethods[5]);
			if (consts.Count != 1)
				return false;
			i3 = consts[0];
			return true;
		}

		bool FindMethodInts() {
			foreach (var nestedType in type.NestedTypes) {
				var methods = GetBinaryIntMethods(nestedType);
				if (methods.Count < 3)
					continue;
				foreach (var m in methods)
					simpleDeobfuscator.Deobfuscate(m);
				if (!FindMethod1Int(methods))
					continue;
				if (!FindMethod2Int(methods))
					continue;
				if (!FindMethod3Int(methods))
					continue;

				return true;
			}
			return false;
		}

		
		static List<MethodDef> GetBinaryIntMethods(TypeDef type) {
			var list = new List<MethodDef>();
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Int32", "(System.Int32,System.Int32)"))
					continue;

				list.Add(method);
			}
			return list;
		}

		bool FindMethod1Int(IEnumerable<MethodDef> methods) {
			foreach (var method in methods) {
				if (CountInstructions(method, Code.Ldarg_0) != 1)
					continue;
				var constants = GetConstants(method);
				if (constants.Count != 1)
					continue;

				m1_i1 = constants[0];
				return true;
			}
			return false;
		}

		bool FindMethod2Int(IEnumerable<MethodDef> methods) {
			foreach (var method in methods) {
				var constants = GetConstants(method);
				if (constants.Count != 2)
					continue;

				m2_i1 = constants[0];
				m2_i2 = constants[1];
				return true;
			}
			return false;
		}

		bool FindMethod3Int(IEnumerable<MethodDef> methods) {
			foreach (var method in methods) {
				if (CountInstructions(method, Code.Ldarg_0) != 2)
					continue;
				var constants = GetConstants(method);
				if (constants.Count != 1)
					continue;

				m3_i1 = constants[0];
				return true;
			}
			return false;
		}

		static int CountInstructions(MethodDef method, Code code) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == code)
					count++;
			}
			return count;
		}

		static List<int> GetConstants(MethodDef method) {
			var list = new List<int>();

			if (method == null)
				return list;

			int index = 0;
			var instrs = method.Body.Instructions;
			var constantsReader = new EfConstantsReader(method);
			while (true) {
				if (!constantsReader.GetNextInt32(ref index, out int val))
					break;

				if (index < instrs.Count && instrs[index].OpCode.Code != Code.Ret)
					list.Add(val);
			}

			return list;
		}

		int BinOp1(int a, int b) => a ^ (b - m1_i1);
		int BinOp2(int a, int b) => (a - m2_i1) ^ (b + m2_i2);
		int BinOp3(int a, int b) => a ^ (b - m3_i1) ^ (a - b);

		int ConstMethod1() =>
			BinOp3(BinOp2(efConstMethods[1].DeclaringType.MDToken.ToInt32(), BinOp3(efConstMethods[0].DeclaringType.MDToken.ToInt32(), efConstMethods[4].DeclaringType.MDToken.ToInt32())), ConstMethod6());

		int ConstMethod2() =>
			BinOp1(efConstMethods[2].DeclaringType.MDToken.ToInt32(), efConstMethods[3].DeclaringType.MDToken.ToInt32() ^ BinOp2(efConstMethods[1].DeclaringType.MDToken.ToInt32(), BinOp3(efConstMethods[5].DeclaringType.MDToken.ToInt32(), ConstMethod4())));

		int ConstMethod3() =>
			BinOp3(BinOp1(ConstMethod2() ^ i1, efConstMethods[3].DeclaringType.MDToken.ToInt32()), BinOp2(efConstMethods[0].DeclaringType.MDToken.ToInt32() ^ efConstMethods[5].DeclaringType.MDToken.ToInt32(), i2));

		int ConstMethod4() =>
			BinOp3(efConstMethods[3].DeclaringType.MDToken.ToInt32(), BinOp1(efConstMethods[0].DeclaringType.MDToken.ToInt32(), BinOp2(efConstMethods[1].DeclaringType.MDToken.ToInt32(), BinOp3(efConstMethods[2].DeclaringType.MDToken.ToInt32(), BinOp1(efConstMethods[4].DeclaringType.MDToken.ToInt32(), efConstMethods[5].DeclaringType.MDToken.ToInt32())))));

		int ConstMethod5() =>
			BinOp2(BinOp2(ConstMethod3(), BinOp1(efConstMethods[4].DeclaringType.MDToken.ToInt32(), ConstMethod2())), efConstMethods[5].DeclaringType.MDToken.ToInt32());

		int ConstMethod6() =>
			BinOp1(efConstMethods[5].DeclaringType.MDToken.ToInt32(), BinOp3(BinOp2(efConstMethods[4].DeclaringType.MDToken.ToInt32(), efConstMethods[0].DeclaringType.MDToken.ToInt32()), BinOp3(efConstMethods[2].DeclaringType.MDToken.ToInt32() ^ i3, ConstMethod5())));

		public ulong GetMagic() {
			if (type == null)
				throw new ApplicationException("Can't calculate magic since type isn't initialized");

			var bytes = new List<byte>();
			if (module.Assembly != null) {
				if (!PublicKeyBase.IsNullOrEmpty2(module.Assembly.PublicKey))
					bytes.AddRange(module.Assembly.PublicKeyToken.Data);
				bytes.AddRange(Encoding.Unicode.GetBytes(module.Assembly.Name.String));
			}

			int num3 = ConstMethod1();
			int num2 = type.MDToken.ToInt32();

			bytes.Add((byte)(num2 >> shiftConsts[0]));
			bytes.Add((byte)(num3 >> shiftConsts[1]));
			bytes.Add((byte)(num2 >> shiftConsts[2]));
			bytes.Add((byte)(num3 >> shiftConsts[3]));
			bytes.Add((byte)(num2 >> shiftConsts[4]));
			bytes.Add((byte)(num3 >> shiftConsts[5]));
			bytes.Add((byte)(num2 >> shiftConsts[6]));
			bytes.Add((byte)(num3 >> shiftConsts[7]));

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
