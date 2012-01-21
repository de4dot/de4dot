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

	class MethodsFinder {
		Dictionary<Module, MethodsModule> moduleToMethods = new Dictionary<Module, MethodsModule>();

		class MethodsModule {
			const int MAX_METHODS = 30;
			List<MethodBase> methods = new List<MethodBase>(MAX_METHODS);
			int next;

			public MethodsModule(Module module) {
				var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

				foreach (var type in module.GetTypes()) {
					if (methods.Count >= MAX_METHODS)
						break;
					foreach (var method in type.GetMethods(flags)) {
						if (methods.Count >= MAX_METHODS)
							break;
						methods.Add(method);
					}
				}

				foreach (var method in module.GetMethods(flags)) {
					if (methods.Count >= MAX_METHODS)
						break;
					methods.Add(method);
				}
			}

			public MethodBase getNext() {
				return methods[next++ % methods.Count];
			}
		}

		public MethodBase getMethod(Module module) {
			MethodsModule methodsModule;
			if (!moduleToMethods.TryGetValue(module, out methodsModule))
				moduleToMethods[module] = methodsModule = new MethodsModule(module);
			return methodsModule.getNext();
		}
	}

	class MethodsRewriter : IMethodsRewriter {
		MethodsFinder methodsFinder = new MethodsFinder();
		Dictionary<MethodBase, NewMethodInfo> realMethodToNewMethod = new Dictionary<MethodBase, NewMethodInfo>();
		Dictionary<NewMethodInfo, MethodBase> newStackMethodDict = new Dictionary<NewMethodInfo, MethodBase>();
		List<NewMethodInfo> newMethodInfos = new List<NewMethodInfo>();

		// There's no documented way to get a dynamic method's MethodInfo. If we name the
		// method and it's a unique random name, we can still find the emulated method.
		Dictionary<string, NewMethodInfo> delegateNameToNewMethodInfo = new Dictionary<string, NewMethodInfo>(StringComparer.Ordinal);

		class NewMethodInfo {
			// Original method
			public MethodBase oldMethod;

			public Type delegateType;

			// The modified code is here
			public Delegate delegateInstance;

			// newMethodInfos index
			public int delegateIndex;

			public RewrittenMethod rewrittenMethod;

			// Name of method used by delegateInstance
			public string delegateMethodName;

			// Name of method used by rewrittenMethod
			public string rewrittenMethodName;

			public NewMethodInfo(MethodBase oldMethod, int delegateIndex, string delegateMethodName, string rewrittenMethodName) {
				this.oldMethod = oldMethod;
				this.delegateIndex = delegateIndex;
				this.delegateMethodName = delegateMethodName;
				this.rewrittenMethodName = rewrittenMethodName;
			}

			public bool isRewrittenMethod(string name) {
				return name == rewrittenMethodName;
			}

			public bool isDelegateMethod(string name) {
				return name == delegateMethodName;
			}
		}

		public Type getDelegateType(MethodBase methodBase) {
			return realMethodToNewMethod[methodBase].delegateType;
		}

		public RewrittenMethod createDelegate(MethodBase realMethod) {
			var newMethodInfo = realMethodToNewMethod[realMethod];
			if (newMethodInfo.rewrittenMethod != null)
				return newMethodInfo.rewrittenMethod;

			var dm = new DynamicMethod(newMethodInfo.rewrittenMethodName, typeof(object), new Type[] { GetType(), typeof(object[]) }, newMethodInfo.oldMethod.Module, true);
			var ilg = dm.GetILGenerator();

			ilg.Emit(ROpCodes.Ldarg_0);
			ilg.Emit(ROpCodes.Ldc_I4, newMethodInfo.delegateIndex);
			ilg.Emit(ROpCodes.Call, GetType().GetMethod("rtGetDelegateInstance", BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance));
			ilg.Emit(ROpCodes.Castclass, newMethodInfo.delegateType);

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

		public void setCaller(RewrittenMethod rewrittenMethod, MethodBase caller) {
			if (caller == null)
				return;
			var newMethodInfo = getNewMethodInfo(rewrittenMethod.Method.Name);
			newStackMethodDict[newMethodInfo] = caller;
		}

		string getDelegateMethodName(MethodBase method) {
			string name = null;
			do {
				name = string.Format(" {0} {1:X8} DMN {2:X8} ", method.Name, method.MetadataToken, Utils.getRandomUint());
			} while (delegateNameToNewMethodInfo.ContainsKey(name));
			return name;
		}

		public void createMethod(MethodBase realMethod) {
			if (realMethodToNewMethod.ContainsKey(realMethod))
				return;
			var newMethodInfo = new NewMethodInfo(realMethod, newMethodInfos.Count, getDelegateMethodName(realMethod), getDelegateMethodName(realMethod));
			newMethodInfos.Add(newMethodInfo);
			delegateNameToNewMethodInfo[newMethodInfo.delegateMethodName] = newMethodInfo;
			delegateNameToNewMethodInfo[newMethodInfo.rewrittenMethodName] = newMethodInfo;
			realMethodToNewMethod[realMethod] = newMethodInfo;

			var moduleInfo = Resolver.loadAssembly(realMethod.Module);
			var methodInfo = moduleInfo.getMethod(realMethod);
			if (!methodInfo.hasInstructions())
				throw new ApplicationException(string.Format("Method {0} ({1:X8}) has no body", methodInfo.methodDefinition, methodInfo.methodDefinition.MetadataToken.ToUInt32()));

			var codeGenerator = new CodeGenerator(this, newMethodInfo.delegateMethodName);
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
					else if (MemberReferenceHelper.verifyType(ctor.DeclaringType, "mscorlib", "System.Diagnostics.StackFrame")) {
						insertLoadThis(block, i + 1);
						insertCallOurMethod(block, i + 2, "static_rtFixStackFrame");
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

					var method = Resolver.getMethod((MethodReference)instr.Operand);
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
								else {
									// Don't cast it to its correct type. This will sometimes cause
									// an exception in some EF obfuscated assembly since we'll be
									// trying to cast a System.Reflection.AssemblyName type to some
									// other type.
									// block.insert(n++, Instruction.Create(OpCodes.Castclass, argType));
								}
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
			int count = method.Parameters.Count + (method.HasImplicitThis ? 1 : 0);
			var list = new List<TypeReference>(count);
			if (method.HasImplicitThis)
				list.Add(method.DeclaringType);
			foreach (var argType in method.Parameters)
				list.Add(argType.ParameterType);
			return list;
		}

		static FieldInfo getStackTraceStackFramesField() {
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return ResolverUtils.getFieldThrow(typeof(StackTrace), typeof(StackFrame[]), flags, "Could not find StackTrace's frames (StackFrame[]) field");
		}

		static FieldInfo getStackFrameMethodField() {
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return ResolverUtils.getFieldThrow(typeof(StackFrame), typeof(MethodBase), flags, "Could not find StackFrame's method (MethodBase) field");
		}

		static void writeMethodBase(StackFrame frame, MethodBase method) {
			var methodField = getStackFrameMethodField();
			methodField.SetValue(frame, method);
			if (frame.GetMethod() != method)
				throw new ApplicationException(string.Format("Could not set new method: {0}", method));
		}

		NewMethodInfo getNewMethodInfo(string name) {
			NewMethodInfo info;
			delegateNameToNewMethodInfo.TryGetValue(name, out info);
			return info;
		}

		// Called after the StackTrace ctor has been called.
		static StackTrace static_rtFixStackTrace(StackTrace stackTrace, MethodsRewriter self) {
			return self.rtFixStackTrace(stackTrace);
		}

		StackTrace rtFixStackTrace(StackTrace stackTrace) {
			var framesField = getStackTraceStackFramesField();
			var frames = (StackFrame[])framesField.GetValue(stackTrace);

			var newFrames = new List<StackFrame>(frames.Length);
			foreach (var frame in frames) {
				fixStackFrame(frame);
				newFrames.Add(frame);
			}

			framesField.SetValue(stackTrace, newFrames.ToArray());
			return stackTrace;
		}

		static StackFrame static_rtFixStackFrame(StackFrame stackFrame, MethodsRewriter self) {
			return self.rtFixStackFrame(stackFrame);
		}

		StackFrame rtFixStackFrame(StackFrame frame) {
			fixStackFrame(frame);
			return frame;
		}

		void fixStackFrame(StackFrame frame) {
			var method = frame.GetMethod();
			var info = getNewMethodInfo(method.Name);
			if (info == null)
				return;

			MethodBase stackMethod;
			if (newStackMethodDict.TryGetValue(info, out stackMethod)) {
				writeMethodBase(frame, stackMethod);
			}
			else if (info.isRewrittenMethod(method.Name)) {
				// Write random method from the same module
				writeMethodBase(frame, methodsFinder.getMethod(info.oldMethod.Module));
			}
			else if (info.isDelegateMethod(method.Name)) {
				// Write original method
				writeMethodBase(frame, info.oldMethod);
			}
			else
				throw new ApplicationException("BUG: Shouldn't be here");
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
			return Assembly.GetAssembly(type);
		}

		Delegate rtGetDelegateInstance(int delegateIndex) {
			return newMethodInfos[delegateIndex].delegateInstance;
		}
	}
}
