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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET {
	class ProxyCallFixer : ProxyCallFixer1 {
		public ProxyCallFixer(ModuleDefMD module)
			: base(module) {
		}

		public ProxyCallFixer(ModuleDefMD module, ProxyCallFixer oldOne)
			: base(module) {
			foreach (var method in oldOne.delegateCreatorMethods)
				SetDelegateCreatorMethod(Lookup(method, "Could not find delegate creator method"));
		}

		public void FindDelegateCreator() {
			foreach (var type in module.Types) {
				var methodName = "System.Void " + type.FullName + "::icgd(System.Int32)";
				foreach (var method in type.Methods) {
					if (method.FullName == methodName) {
						SetDelegateCreatorMethod(method);
						return;
					}
				}
			}
		}

		protected override object CheckCctor(ref TypeDef type, MethodDef cctor) {
			var instrs = cctor.Body.Instructions;
			if (instrs.Count != 3)
				return null;
			if (!instrs[0].IsLdcI4())
				return null;
			if (instrs[1].OpCode != OpCodes.Call || !IsDelegateCreatorMethod(instrs[1].Operand as MethodDef))
				return null;
			if (instrs[2].OpCode != OpCodes.Ret)
				return null;

			int delegateToken = 0x02000001 + instrs[0].GetLdcI4Value();
			if (type.MDToken.ToInt32() != delegateToken) {
				Logger.w("Delegate token is not current type");
				return null;
			}

			return new object();
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			var name = field.Name.String;
			callOpcode = OpCodes.Call;
			if (name.EndsWith("%", StringComparison.Ordinal)) {
				callOpcode = OpCodes.Callvirt;
				name = name.TrimEnd(new char[] { '%' });
			}
			byte[] value = Convert.FromBase64String(name);
			int methodIndex = BitConverter.ToInt32(value, 0);	// 0-based memberRef index
			var mr = module.ResolveMemberRef((uint)methodIndex + 1);
			if (mr == null || !mr.IsMethodRef)
				throw new ApplicationException(string.Format("Invalid MemberRef index: {0}", methodIndex));
			calledMethod = mr;
		}
	}
}
