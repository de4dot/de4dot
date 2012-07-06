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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CliSecure {
	class ProxyCallFixer : ProxyCallFixer1 {
		IList<MemberReference> memberReferences;

		public ProxyCallFixer(ModuleDefinition module)
			: base(module) {
		}

		public ProxyCallFixer(ModuleDefinition module, ProxyCallFixer oldOne)
			: base(module) {
			foreach (var method in oldOne.delegateCreatorMethods)
				setDelegateCreatorMethod(lookup(method, "Could not find delegate creator method"));
		}

		public void findDelegateCreator() {
			foreach (var type in module.Types) {
				var methodName = "System.Void " + type.FullName + "::icgd(System.Int32)";
				foreach (var method in type.Methods) {
					if (method.FullName == methodName) {
						setDelegateCreatorMethod(method);
						return;
					}
				}
			}
		}

		protected override object checkCctor(ref TypeDefinition type, MethodDefinition cctor) {
			var instrs = cctor.Body.Instructions;
			if (instrs.Count != 3)
				return null;
			if (!DotNetUtils.isLdcI4(instrs[0].OpCode.Code))
				return null;
			if (instrs[1].OpCode != OpCodes.Call || !isDelegateCreatorMethod(instrs[1].Operand as MethodDefinition))
				return null;
			if (instrs[2].OpCode != OpCodes.Ret)
				return null;

			int delegateToken = 0x02000001 + DotNetUtils.getLdcI4Value(instrs[0]);
			if (type.MetadataToken.ToInt32() != delegateToken) {
				Log.w("Delegate token is not current type");
				return null;
			}

			return new object();
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			if (memberReferences == null)
				memberReferences = new List<MemberReference>(module.GetMemberReferences());

			var name = field.Name;
			callOpcode = OpCodes.Call;
			if (name.EndsWith("%", StringComparison.Ordinal)) {
				callOpcode = OpCodes.Callvirt;
				name = name.TrimEnd(new char[] { '%' });
			}
			byte[] value = Convert.FromBase64String(name);
			int methodIndex = BitConverter.ToInt32(value, 0);	// 0-based memberRef index
			if (methodIndex >= memberReferences.Count)
				throw new ApplicationException(string.Format("methodIndex ({0}) >= memberReferences.Count ({1})", methodIndex, memberReferences.Count));
			calledMethod = memberReferences[methodIndex] as MethodReference;
		}
	}
}
