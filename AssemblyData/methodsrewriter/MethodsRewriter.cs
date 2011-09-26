/*
    Copyright (C) 2011 de4dot@gmail.com

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
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

using OpCode = Mono.Cecil.Cil.OpCode;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ROpCode = System.Reflection.Emit.OpCode;
using ROpCodes = System.Reflection.Emit.OpCodes;

namespace AssemblyData.methodsrewriter {
	delegate object RewrittenMethod(object[] args);

	class MethodsRewriter : IMethodsRewriter {
		Dictionary<Module, MModule> modules = new Dictionary<Module, MModule>();
		Dictionary<MethodBase, NewMethodInfo> realMethodToNewMethod = new Dictionary<MethodBase, NewMethodInfo>();
		List<NewMethodInfo> newMethodInfos = new List<NewMethodInfo>();

		class NewMethodInfo {
			// Original method
			public MethodBase oldMethod;

			public Type delegateType;

			// The modified code is here
			public Delegate delegateInstance;

			// newMethodInfos index
			public int delegateIndex;

			public RewrittenMethod rewrittenMethod;

			public NewMethodInfo(MethodBase oldMethod) {
				this.oldMethod = oldMethod;
			}
		}

		MModule loadAssembly(Module module) {
			MModule info;
			if (modules.TryGetValue(module, out info))
				return info;

			info = new MModule(module, ModuleDefinition.ReadModule(module.FullyQualifiedName));
			modules[module] = info;
			return info;
		}

		MModule getModule(ModuleDefinition moduleDefinition) {
			foreach (var mm in modules.Values) {
				if (mm.moduleDefinition == moduleDefinition)
					return mm;
			}
			return null;
		}

		MModule getModule(AssemblyNameReference assemblyRef) {
			foreach (var mm in modules.Values) {
				var asm = mm.moduleDefinition.Assembly;
				if (asm.Name.FullName == assemblyRef.FullName)
					return mm;
			}
			return null;
		}

		MModule getModule(IMetadataScope scope) {
			if (scope is ModuleDefinition)
				return getModule((ModuleDefinition)scope);
			else if (scope is AssemblyNameReference)
				return getModule((AssemblyNameReference)scope);

			return null;
		}

		MType getType(TypeReference typeReference) {
			var module = getModule(typeReference.Scope);
			if (module != null)
				return module.getType(typeReference);
			return null;
		}

		MMethod getMethod(MethodReference methodReference) {
			var module = getModule(methodReference.DeclaringType.Scope);
			if (module != null)
				return module.getMethod(methodReference);
			return null;
		}

		MField getField(FieldReference fieldReference) {
			var module = getModule(fieldReference.DeclaringType.Scope);
			if (module != null)
				return module.getField(fieldReference);
			return null;
		}

		public object getRtObject(MemberReference memberReference) {
			if (memberReference is TypeReference)
				return getRtType((TypeReference)memberReference);
			else if (memberReference is FieldReference)
				return getRtField((FieldReference)memberReference);
			else if (memberReference is MethodReference)
				return getRtMethod((MethodReference)memberReference);

			throw new ApplicationException(string.Format("Unknown MemberReference: {0}", memberReference));
		}

		public Type getRtType(TypeReference typeReference) {
			var mtype = getType(typeReference);
			if (mtype != null)
				return mtype.type;

			return Resolver.resolve(typeReference);
		}

		public FieldInfo getRtField(FieldReference fieldReference) {
			var mfield = getField(fieldReference);
			if (mfield != null)
				return mfield.fieldInfo;

			return Resolver.resolve(fieldReference);
		}

		public MethodBase getRtMethod(MethodReference methodReference) {
			var mmethod = getMethod(methodReference);
			if (mmethod != null)
				return mmethod.methodBase;

			return Resolver.resolve(methodReference);
		}

		public Type getDelegateType(MethodBase methodBase) {
			return realMethodToNewMethod[methodBase].delegateType;
		}

		public RewrittenMethod createDelegate(MethodBase realMethod) {
			var newMethodInfo = realMethodToNewMethod[realMethod];
			if (newMethodInfo.rewrittenMethod != null)
				return newMethodInfo.rewrittenMethod;

			var dm = new DynamicMethod("method_" + newMethodInfo.oldMethod.Name, typeof(object), new Type[] { GetType(), typeof(object[]) }, GetType(), true);
			var ilg = dm.GetILGenerator();

			ilg.Emit(ROpCodes.Ldarg_0);
			ilg.Emit(ROpCodes.Ldc_I4, newMethodInfo.delegateIndex);
			ilg.Emit(ROpCodes.Call, GetType().GetMethod("rtGetDelegateInstance", BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance));

			var args = newMethodInfo.oldMethod.GetParameters();
			for (int i = 0; i < args.Length; i++) {
				var arg = args[i].ParameterType;

				ilg.Emit(ROpCodes.Ldarg_1);
				ilg.Emit(ROpCodes.Ldc_I4, i);
				ilg.Emit(ROpCodes.Ldelem_Ref);

				if (arg.IsValueType)
					ilg.Emit(ROpCodes.Unbox_Any, arg);
				else
					ilg.Emit(ROpCodes.Castclass, arg);
			}
			ilg.Emit(ROpCodes.Ldarg_0);

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;
			var invokeMethod = newMethodInfo.delegateType.GetMethod("Invoke", flags);
			ilg.Emit(ROpCodes.Call, invokeMethod);
			if (ResolverUtils.getReturnType(newMethodInfo.oldMethod) == typeof(void))
				ilg.Emit(ROpCodes.Ldnull);
			ilg.Emit(ROpCodes.Ret);

			newMethodInfo.rewrittenMethod = (RewrittenMethod)dm.CreateDelegate(typeof(RewrittenMethod), this);
			return newMethodInfo.rewrittenMethod;
		}

		public void createMethod(MethodBase realMethod) {
			if (realMethodToNewMethod.ContainsKey(realMethod))
				return;
			var newMethodInfo = new NewMethodInfo(realMethod);
			newMethodInfo.delegateIndex = newMethodInfos.Count;
			newMethodInfos.Add(newMethodInfo);
			realMethodToNewMethod[realMethod] = newMethodInfo;

			var moduleInfo = loadAssembly(realMethod.Module);
			var methodInfo = moduleInfo.getMethod(realMethod);
			if (!methodInfo.methodDefinition.HasBody || methodInfo.methodDefinition.Body.Instructions.Count == 0)
				throw new ApplicationException(string.Format("Method {0} ({1:X8}) has no body", methodInfo.methodDefinition, methodInfo.methodDefinition.MetadataToken.ToUInt32()));

			var codeGenerator = new CodeGenerator(this);
			codeGenerator.setMethodInfo(methodInfo);
			newMethodInfo.delegateType = codeGenerator.DelegateType;

			var blocks = new Blocks(methodInfo.methodDefinition);
			foreach (var block in blocks.MethodBlocks.getAllBlocks())
				update(block, newMethodInfo);

			IList<Instruction> allInstructions;
			IList<ExceptionHandler> allExceptionHandlers;
			blocks.getCode(out allInstructions, out allExceptionHandlers);
			newMethodInfo.delegateInstance = codeGenerator.generate(allInstructions, allExceptionHandlers);
		}

		static Instruction create(OpCode opcode, object operand) {
			return new Instruction {
				OpCode = opcode,
				Operand = operand,
			};
		}

		// Inserts ldarg THIS, and returns number of instructions inserted at 'i'
		int insertLoadThis(Block block, int i) {
			block.insert(i, create(OpCodes.Ldarg, new Operand(Operand.Type.ThisArg)));
			return 1;
		}

		int insertCallOurMethod(Block block, int i, string methodName) {
			block.insert(i, create(OpCodes.Call, new Operand(Operand.Type.OurMethod, methodName)));
			return 1;
		}

		void update(Block block, NewMethodInfo currentMethodInfo) {
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode == OpCodes.Newobj) {
					var ctor = (MethodReference)instr.Operand;
					if (MemberReferenceHelper.verifyType(ctor.DeclaringType, "mscorlib", "System.Diagnostics.StackTrace")) {
						insertLoadThis(block, i + 1);
						insertCallOurMethod(block, i + 2, "static_rtFixStackTrace");
						i += 2;
						continue;
					}
				}

				if (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) {
					var calledMethod = (MethodReference)instr.Operand;
					if (DotNetUtils.isSameAssembly(calledMethod.DeclaringType, "mscorlib")) {
						if (calledMethod.ToString() == "System.Reflection.Assembly System.Reflection.Assembly::GetAssembly(System.Type)") {
							block.replace(i, 1, Instruction.Create(OpCodes.Nop));
							insertLoadThis(block, i + 1);
							insertCallOurMethod(block, i + 2, "static_rtGetAssembly_TypeArg");
							i += 2;
							continue;
						}
						else if (calledMethod.ToString() == "System.Reflection.Assembly System.Reflection.Assembly::GetCallingAssembly()" ||
								calledMethod.ToString() == "System.Reflection.Assembly System.Reflection.Assembly::GetEntryAssembly()" ||
								calledMethod.ToString() == "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()") {
							block.replace(i, 1, Instruction.Create(OpCodes.Nop));
							insertLoadThis(block, i + 1);
							block.insert(i + 2, Instruction.Create(OpCodes.Ldc_I4, currentMethodInfo.delegateIndex));
							insertCallOurMethod(block, i + 3, "rtGetAssembly");
							i += 3;
							continue;
						}
					}

					var method = getMethod((MethodReference)instr.Operand);
					if (method != null) {
						createMethod(method.methodBase);
						var newMethodInfo = realMethodToNewMethod[method.methodBase];

						block.replace(i, 1, Instruction.Create(OpCodes.Nop));
						int n = i + 1;

						// Pop all pushed args to a temp array
						var mparams = getParameters(method.methodDefinition);
						if (mparams.Count > 0) {
							block.insert(n++, Instruction.Create(OpCodes.Ldc_I4, mparams.Count));
							var objectType = method.methodDefinition.Module.TypeSystem.Object;
							block.insert(n++, Instruction.Create(OpCodes.Newarr, objectType));
							block.insert(n++, create(OpCodes.Stloc, new Operand(Operand.Type.TempObjArray)));

							for (int j = mparams.Count - 1; j >= 0; j--) {
								var argType = mparams[j];
								if (argType.IsValueType)
									block.insert(n++, Instruction.Create(OpCodes.Box, argType));
								block.insert(n++, create(OpCodes.Stloc, new Operand(Operand.Type.TempObj)));
								block.insert(n++, create(OpCodes.Ldloc, new Operand(Operand.Type.TempObjArray)));
								block.insert(n++, Instruction.Create(OpCodes.Ldc_I4, j));
								block.insert(n++, create(OpCodes.Ldloc, new Operand(Operand.Type.TempObj)));
								block.insert(n++, Instruction.Create(OpCodes.Stelem_Ref));
							}
						}

						// Push delegate instance
						insertLoadThis(block, n++);
						block.insert(n++, Instruction.Create(OpCodes.Ldc_I4, newMethodInfo.delegateIndex));
						insertCallOurMethod(block, n++, "rtGetDelegateInstance");
						block.insert(n++, create(OpCodes.Castclass, new Operand(Operand.Type.ReflectionType, newMethodInfo.delegateType)));

						// Push all popped args
						if (mparams.Count > 0) {
							for (int j = 0; j < mparams.Count; j++) {
								block.insert(n++, create(OpCodes.Ldloc, new Operand(Operand.Type.TempObjArray)));
								block.insert(n++, Instruction.Create(OpCodes.Ldc_I4, j));
								block.insert(n++, Instruction.Create(OpCodes.Ldelem_Ref));
								var argType = mparams[j];
								if (argType.IsValueType)
									block.insert(n++, Instruction.Create(OpCodes.Unbox_Any, argType));
								else
									block.insert(n++, Instruction.Create(OpCodes.Castclass, argType));
							}
						}

						insertLoadThis(block, n++);
						block.insert(n++, create(OpCodes.Call, new Operand(Operand.Type.NewMethod, method.methodBase)));
						i = n - 1;
						continue;
					}
				}
			}
		}

		static List<TypeReference> getParameters(MethodDefinition method) {
			int count = method.Parameters.Count + (method.HasThis ? 1 : 0);
			var list = new List<TypeReference>(count);
			if (method.HasThis)
				list.Add(method.DeclaringType);
			foreach (var argType in method.Parameters)
				list.Add(argType.ParameterType);
			return list;
		}

		// Called after the StackTrace ctor has been called.
		static StackTrace static_rtFixStackTrace(StackTrace stackTrace, MethodsRewriter self) {
			return self.rtFixStackTrace(stackTrace);
		}

		StackTrace rtFixStackTrace(StackTrace stackTrace) {
			//TODO:
			return stackTrace;
		}

		// Called when the code calls GetCallingAssembly(), GetEntryAssembly(), or GetExecutingAssembly()
		Assembly rtGetAssembly(int delegateIndex) {
			return newMethodInfos[delegateIndex].oldMethod.Module.Assembly;
		}

		// Called when the code calls GetAssembly(Type)
		static Assembly static_rtGetAssembly_TypeArg(Type type, MethodsRewriter self) {
			return self.rtGetAssembly_TypeArg(type);
		}

		Assembly rtGetAssembly_TypeArg(Type type) {
			return Assembly.GetAssembly(type);	//TODO:
		}

		Delegate rtGetDelegateInstance(int delegateIndex) {
			return newMethodInfos[delegateIndex].delegateInstance;
		}
	}
}
