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

using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class ProxyCallFixer : ProxyCallFixer3 {
		ISimpleDeobfuscator simpleDeobfuscator;

		public ProxyCallFixer(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module) {
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public void findDelegateCreator() {
			foreach (var type in module.Types) {
				var creatorMethod = checkType(type);
				if (creatorMethod == null)
					continue;

				setDelegateCreatorMethod(creatorMethod);
				return;
			}
		}

		static readonly string[] requiredFields = new string[] {
			"System.Reflection.Module",
		};
		static MethodDefinition checkType(TypeDefinition type) {
			if (!new FieldTypes(type).exactly(requiredFields))
				return null;
			if (DotNetUtils.getMethod(type, ".cctor") == null)
				return null;

			return checkMethods(type);
		}

		static MethodDefinition checkMethods(TypeDefinition type) {
			MethodDefinition creatorMethod = null;
			foreach (var method in type.Methods) {
				if (method.Body == null)
					return null;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "(System.Int32)"))
					return null;
				if (!DeobUtils.hasInteger(method, 0x02000000))
					return null;
				if (!DeobUtils.hasInteger(method, 0x06000000))
					return null;
				creatorMethod = method;
			}
			return creatorMethod;
		}

		protected override object checkCctor(ref TypeDefinition type, MethodDefinition cctor) {
			simpleDeobfuscator.deobfuscate(cctor);
			var realType = getDelegateType(cctor);
			if (realType == null)
				return null;
			type = realType;

			return this;
		}

		TypeDefinition getDelegateType(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null || !isDelegateCreatorMethod(calledMethod))
					continue;

				return module.LookupToken(0x02000000 + DotNetUtils.getLdcI4Value(ldci4)) as TypeDefinition;
			}
			return null;
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			calledMethod = module.LookupToken(0x06000000 + field.MetadataToken.ToInt32()) as MethodReference;
			callOpcode = OpCodes.Call;
		}
	}
}
