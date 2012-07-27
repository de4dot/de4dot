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

namespace de4dot.code.deobfuscators.Babel_NET {
	class ProxyCallFixer : ProxyCallFixer2 {
		MethodDefinitionAndDeclaringTypeDict<ProxyCreatorType> methodToType = new MethodDefinitionAndDeclaringTypeDict<ProxyCreatorType>();

		public ProxyCallFixer(ModuleDefinition module)
			: base(module) {
		}

		enum ProxyCreatorType {
			None,
			CallOrCallvirt,
			Newobj,
		}

		class Context {
			public TypeReference delegateType;
			public int methodToken;
			public int declaringTypeToken;
			public ProxyCreatorType proxyCreatorType;
			public Context(TypeReference delegateType, int methodToken, int declaringTypeToken, ProxyCreatorType proxyCreatorType) {
				this.delegateType = delegateType;
				this.methodToken = methodToken;
				this.declaringTypeToken = declaringTypeToken;
				this.proxyCreatorType = proxyCreatorType;
			}
		}

		protected override bool ProxyCallIsObfuscated {
			get { return true; }
		}

		protected override object checkCctor(TypeDefinition type, MethodDefinition cctor) {
			var instructions = cctor.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				TypeReference delegateType;
				FieldReference delegateField;
				MethodReference createMethod;
				int methodToken, declaringTypeToken;
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Ldtoken, OpCodes.Ldc_I4, OpCodes.Ldc_I4, OpCodes.Ldtoken, OpCodes.Call);
				if (instrs != null) {
					delegateType = instrs[0].Operand as TypeReference;
					methodToken = DotNetUtils.getLdcI4Value(instrs[1]);
					declaringTypeToken = DotNetUtils.getLdcI4Value(instrs[2]);
					delegateField = instrs[3].Operand as FieldReference;
					createMethod = instrs[4].Operand as MethodReference;
				}
				else if ((instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Ldtoken, OpCodes.Ldc_I4, OpCodes.Ldtoken, OpCodes.Call)) != null) {
					delegateType = instrs[0].Operand as TypeReference;
					methodToken = DotNetUtils.getLdcI4Value(instrs[1]);
					declaringTypeToken = -1;
					delegateField = instrs[2].Operand as FieldReference;
					createMethod = instrs[3].Operand as MethodReference;
				}
				else
					continue;

				if (delegateType == null)
					continue;
				if (delegateField == null)
					continue;
				if (createMethod == null)
					continue;
				var proxyCreatorType = methodToType.find(createMethod);
				if (proxyCreatorType == ProxyCreatorType.None)
					continue;

				return new Context(delegateType, methodToken, declaringTypeToken, proxyCreatorType);
			}

			return null;
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			var ctx = (Context)context;

			switch (ctx.proxyCreatorType) {
			case ProxyCreatorType.CallOrCallvirt:
				callOpcode = field.IsAssembly ? OpCodes.Callvirt : OpCodes.Call;
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
			var requiredTypes = new string[] {
				"System.ModuleHandle",
			};
			foreach (var type in module.Types) {
				if (!new FieldTypes(type).exactly(requiredTypes))
					continue;

				foreach (var method in type.Methods) {
					if (!method.IsStatic || method.Body == null)
						continue;
					if (!DotNetUtils.isMethod(method, "System.Void", "(System.RuntimeTypeHandle,System.Int32,System.RuntimeFieldHandle)") &&
						!DotNetUtils.isMethod(method, "System.Void", "(System.RuntimeTypeHandle,System.Int32,System.Int32,System.RuntimeFieldHandle)"))
						continue;
					var creatorType = getProxyCreatorType(method);
					if (creatorType == ProxyCreatorType.None)
						continue;

					methodToType.add(method, creatorType);
					setDelegateCreatorMethod(method);
				}

				if (methodToType.Count == 0)
					continue;

				return;
			}
		}

		ProxyCreatorType getProxyCreatorType(MethodDefinition methodToCheck) {
			foreach (var calledMethod in DotNetUtils.getCalledMethods(module, methodToCheck)) {
				if (!calledMethod.IsStatic || calledMethod.Body == null)
					continue;
				if (!MemberReferenceHelper.compareTypes(methodToCheck.DeclaringType, calledMethod.DeclaringType))
					continue;
				if (DotNetUtils.isMethod(calledMethod, "System.Void", "(System.Reflection.FieldInfo,System.Type,System.Reflection.MethodInfo)"))
					return ProxyCreatorType.CallOrCallvirt;
				if (DotNetUtils.isMethod(calledMethod, "System.Void", "(System.Reflection.FieldInfo,System.Type,System.Reflection.ConstructorInfo)"))
					return ProxyCreatorType.Newobj;
			}
			return ProxyCreatorType.None;
		}
	}
}
