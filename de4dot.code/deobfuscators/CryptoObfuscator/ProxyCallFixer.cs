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

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class ProxyCallFixer : ProxyCallFixer2 {
		Dictionary<MethodDefinition, ProxyCreatorType> methodToType = new Dictionary<MethodDefinition, ProxyCreatorType>();

		public ProxyCallFixer(ModuleDefinition module)
			: base(module) {
		}

		enum ProxyCreatorType {
			None,
			CallOrCallvirt,
			CallCtor,
			Newobj,
		}

		class Context {
			public int typeToken;
			public int methodToken;
			public int declaringTypeToken;
			public ProxyCreatorType proxyCreatorType;
			public Context(int typeToken, int methodToken, int declaringTypeToken, ProxyCreatorType proxyCreatorType) {
				this.typeToken = typeToken;
				this.methodToken = methodToken;
				this.declaringTypeToken = declaringTypeToken;
				this.proxyCreatorType = proxyCreatorType;
			}
		}

		protected override object checkCctor(TypeDefinition type, MethodDefinition cctor) {
			var instructions = cctor.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Ldc_I4, OpCodes.Ldc_I4, OpCodes.Ldc_I4, OpCodes.Call);
				if (instrs == null)
					continue;

				int typeToken = (int)instrs[0].Operand;
				int methodToken = (int)instrs[1].Operand;
				int declaringTypeToken = (int)instrs[2].Operand;
				var createMethod = instrs[3].Operand as MethodDefinition;

				ProxyCreatorType proxyCreatorType;
				if (!methodToType.TryGetValue(createMethod, out proxyCreatorType))
					continue;

				return new Context(typeToken, methodToken, declaringTypeToken, proxyCreatorType);
			}

			return null;
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			var ctx = (Context)context;

			switch (ctx.proxyCreatorType) {
			case ProxyCreatorType.CallOrCallvirt:
				callOpcode = field.IsFamilyOrAssembly ? OpCodes.Callvirt : OpCodes.Call;
				break;
			case ProxyCreatorType.CallCtor:
				callOpcode = OpCodes.Call;
				break;
			case ProxyCreatorType.Newobj:
				callOpcode = OpCodes.Newobj;
				break;
			default:
				throw new ApplicationException(string.Format("Invalid proxy creator type: {0}", ctx.proxyCreatorType));
			}

			calledMethod = module.LookupToken(ctx.methodToken) as MethodReference;
		}

		public void findDelegateCreator() {
			foreach (var type in module.Types) {
				var createMethod = getProxyCreateMethod(type);
				if (createMethod == null)
					continue;

				var proxyCreatorType = getProxyCreatorType(type, createMethod);
				if (proxyCreatorType == ProxyCreatorType.None)
					continue;
				methodToType[createMethod] = proxyCreatorType;
				setDelegateCreatorMethod(createMethod);
			}
		}

		MethodDefinition getProxyCreateMethod(TypeDefinition type) {
			if (DotNetUtils.findFieldType(type, "System.ModuleHandle", true) == null)
				return null;
			if (type.Fields.Count < 1 || type.Fields.Count > 10)
				return null;

			MethodDefinition createMethod = null;
			foreach (var m in type.Methods) {
				if (m.Name == ".ctor" || m.Name == ".cctor")
					continue;
				if (createMethod == null && DotNetUtils.isMethod(m, "System.Void", "(System.Int32,System.Int32,System.Int32)")) {
					createMethod = m;
					continue;
				}
				continue;
			}
			if (createMethod == null || !createMethod.HasBody)
				return null;
			if (!DeobUtils.hasInteger(createMethod, 0xFFFFFF))
				return null;

			return createMethod;
		}

		ProxyCreatorType getProxyCreatorType(TypeDefinition type, MethodDefinition createMethod) {
			int numCalls = 0, numCallvirts = 0, numNewobjs = 0;
			foreach (var instr in createMethod.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldsfld)
					continue;
				var field = instr.Operand as FieldReference;
				if (field == null)
					continue;
				switch (field.FullName) {
				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Call":
					numCalls++;
					break;
				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Callvirt":
					numCallvirts++;
					break;
				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Newobj":
					numNewobjs++;
					break;
				}
			}

			if (numCalls == 1 && numCallvirts == 1 && numNewobjs == 0)
				return ProxyCreatorType.CallOrCallvirt;
			if (numCalls == 1 && numCallvirts == 0 && numNewobjs == 0)
				return ProxyCreatorType.CallCtor;
			if (numCalls == 0 && numCallvirts == 0 && numNewobjs == 1)
				return ProxyCreatorType.Newobj;
			return ProxyCreatorType.None;
		}
	}
}
