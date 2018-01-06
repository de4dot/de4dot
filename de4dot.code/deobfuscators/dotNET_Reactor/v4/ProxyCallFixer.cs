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

using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class ProxyCallFixer : ProxyCallFixer3 {
		ISimpleDeobfuscator simpleDeobfuscator;

		public ProxyCallFixer(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module) {
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public void FindDelegateCreator() {
			foreach (var type in module.Types) {
				var creatorMethod = CheckType(type);
				if (creatorMethod == null)
					continue;

				SetDelegateCreatorMethod(creatorMethod);
				return;
			}
		}

		static readonly string[] requiredFields = new string[] {
			"System.Reflection.Module",
		};
		static MethodDef CheckType(TypeDef type) {
			if (!new FieldTypes(type).Exactly(requiredFields))
				return null;
			if (type.FindStaticConstructor() == null)
				return null;

			return CheckMethods(type);
		}

		static MethodDef CheckMethods(TypeDef type) {
			MethodDef creatorMethod = null;
			foreach (var method in type.Methods) {
				if (method.Body == null)
					return null;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "(System.Int32)"))
					return null;
				if (!DeobUtils.HasInteger(method, 0x02000000))
					return null;
				if (!DeobUtils.HasInteger(method, 0x06000000))
					return null;
				creatorMethod = method;
			}
			return creatorMethod;
		}

		protected override object CheckCctor(ref TypeDef type, MethodDef cctor) {
			simpleDeobfuscator.Deobfuscate(cctor);
			var realType = GetDelegateType(cctor);
			if (realType == null)
				return null;
			type = realType;

			return this;
		}

		TypeDef GetDelegateType(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4())
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || !IsDelegateCreatorMethod(calledMethod))
					continue;

				return module.ResolveToken(0x02000000 + ldci4.GetLdcI4Value()) as TypeDef;
			}
			return null;
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			calledMethod = module.ResolveToken(0x06000000 + field.MDToken.ToInt32()) as IMethod;
			callOpcode = OpCodes.Call;
		}
	}
}
