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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	interface IDynocodeGenerator {
		IEnumerable<int> getValues(int input);
	}

	// Something added in EF 3.5 which they call Dynocode. The string decrypter can now
	// call some iterator classes that will return some integers that it will use to
	// XOR some key.
	class Dynocode {
		ISimpleDeobfuscator simpleDeobfuscator;
		Dictionary<TypeDef, IDynocodeGenerator> typeToDCGen = new Dictionary<TypeDef, IDynocodeGenerator>();

		class DCGen1 : IDynocodeGenerator {
			public int magic1;
			public int magic2;
			public int magic3;
			public int magic4;
			public int magic5;
			public int magic6;
			public int magic7;

			public IEnumerable<int> getValues(int input) {
				yield return magic1;
				yield return magic2;
				yield return input ^ magic3;
				yield return magic4;
				yield return magic5;
				yield return magic6;
				yield return input ^ magic7;
			}
		}

		class DCGen2 : IDynocodeGenerator {
			public int magic1;

			public IEnumerable<int> getValues(int input) {
				int x = 0;
				int y = 1;
				while (true) {
					yield return y;
					if (--input == 0)
						break;
					int tmp = y;
					y = (x + y + input) ^ magic1;
					x = tmp;
				}
			}
		}

		class DCGen3 : IDynocodeGenerator {
			public int magic1;
			public int magic2;
			public DCGen2 dc2;

			public IEnumerable<int> getValues(int input) {
				int i = 7;
				foreach (var val in dc2.getValues(input)) {
					int x = val ^ input;
					if ((x % 4) == 0)
						x ^= magic1;
					if ((x % 16) == 0)
						x ^= magic2;
					yield return x;
					if (--i == 0)
						break;
				}
			}
		}

		public IEnumerable<TypeDef> Types {
			get {
				foreach (var type in typeToDCGen.Keys)
					yield return type.DeclaringType;
			}
		}

		public Dynocode(ISimpleDeobfuscator simpleDeobfuscator) {
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public IDynocodeGenerator getDynocodeGenerator(TypeDef type) {
			if (type == null)
				return null;
			var dt = type.DeclaringType;
			if (dt == null)
				return null;
			IDynocodeGenerator dcGen;
			if (typeToDCGen.TryGetValue(type, out dcGen))
				return dcGen;

			if (dt.NestedTypes.Count == 1)
				dcGen = getDCGen1(type);
			else if (dt.NestedTypes.Count == 2)
				dcGen = getDCGen3(type);

			typeToDCGen[type] = dcGen;

			return dcGen;
		}

		DCGen1 getDCGen1(TypeDef type) {
			var method = getMoveNext(type);
			if (method == null)
				return null;
			simpleDeobfuscator.deobfuscate(method);
			var swLabels = getSwitchLabels(method);
			if (swLabels == null || swLabels.Count < 7)
				return null;

			var dcGen = new DCGen1();
			if (!getMagicDC1(method, swLabels[0], out dcGen.magic1))
				return null;
			if (!getMagicDC1(method, swLabels[1], out dcGen.magic2))
				return null;
			if (!getMagicXorDC1(method, swLabels[2], out dcGen.magic3))
				return null;
			if (!getMagicDC1(method, swLabels[3], out dcGen.magic4))
				return null;
			if (!getMagicDC1(method, swLabels[4], out dcGen.magic5))
				return null;
			if (!getMagicDC1(method, swLabels[5], out dcGen.magic6))
				return null;
			if (!getMagicXorDC1(method, swLabels[6], out dcGen.magic7))
				return null;

			return dcGen;
		}

		static IList<Instruction> getSwitchLabels(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Switch)
					continue;
				return instr.Operand as IList<Instruction>;
			}
			return null;
		}

		static bool getMagicDC1(MethodDef method, Instruction target, out int magic) {
			magic = 0;
			var instrs = method.Body.Instructions;
			int index = instrs.IndexOf(target);
			if (index < 0)
				return false;

			for (int i = index; i < instrs.Count - 3; i++) {
				var instr = instrs[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					return false;
				if (instr.OpCode.Code != Code.Stfld)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Ldarg_0)
					continue;
				var ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Stfld)
					continue;

				magic = ldci4.GetLdcI4Value();
				return true;
			}

			return false;
		}

		static bool getMagicXorDC1(MethodDef method, Instruction target, out int magic) {
			magic = 0;
			var instrs = method.Body.Instructions;
			int index = instrs.IndexOf(target);
			if (index < 0)
				return false;

			for (int i = index; i < instrs.Count - 2; i++) {
				var instr = instrs[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					return false;
				if (!instr.IsLdcI4())
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Xor)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Stfld)
					continue;

				magic = instr.GetLdcI4Value();
				return true;
			}

			return false;
		}

		DCGen3 getDCGen3(TypeDef type) {
			var method = getMoveNext(type);
			if (method == null)
				return null;
			simpleDeobfuscator.deobfuscate(method);

			var dcGen = new DCGen3();
			int index = 0;
			if (!getMagicDC3(method, ref index, out dcGen.magic1))
				return null;
			if (!getMagicDC3(method, ref index, out dcGen.magic2))
				return null;

			var dt = type.DeclaringType;
			dcGen.dc2 = getDCGen2(dt.NestedTypes[0] == type ? dt.NestedTypes[1] : dt.NestedTypes[0]);

			return dcGen;
		}

		static bool getMagicDC3(MethodDef method, ref int index, out int magic) {
			var instrs = method.Body.Instructions;
			for (int i = index; i < instrs.Count - 2; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Xor)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Stfld)
					continue;

				index = i + 3;
				magic = ldci4.GetLdcI4Value();
				return true;
			}

			magic = 0;
			return false;
		}

		DCGen2 getDCGen2(TypeDef type) {
			var method = getMoveNext(type);
			if (method == null)
				return null;
			simpleDeobfuscator.deobfuscate(method);

			var dcGen = new DCGen2();
			int index = 0;
			if (!getMagicDC3(method, ref index, out dcGen.magic1))
				return null;

			return dcGen;
		}

		static MethodDef getMoveNext(TypeDef type) {
			foreach (var m in type.Methods) {
				if (!m.IsVirtual)
					continue;
				foreach (var mo in m.Overrides) {
					if (mo.MethodDeclaration.FullName == "System.Boolean System.Collections.IEnumerator::MoveNext()")
						return m;
				}
			}
			foreach (var m in type.Methods) {
				if (!m.IsVirtual)
					continue;
				if (m.Name != "MoveNext")
					continue;
				if (!DotNetUtils.isMethod(m, "System.Boolean", "()"))
					continue;
				return m;
			}
			return null;
		}
	}
}
